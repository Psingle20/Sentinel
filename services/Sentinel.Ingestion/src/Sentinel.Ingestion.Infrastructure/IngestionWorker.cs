using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sentinel.Ingestion.Application;
using Sentinel.Ingestion.Application.Providers;

namespace Sentinel.Ingestion.Infrastructure;

/// <summary>
/// Pulls every configured source once per poll interval, each in its own loop, so a slow or
/// failing source never delays another: a large log pull cannot hold back the SLO signals
/// detection depends on (Phase 02 stream prioritization). Loops start in priority order. Each pull
/// runs in its own scope and unit of work, and no loop queues work in memory. When the control plane
/// publishes new sources (Phase 08), the loops finish their current pulls and restart with them.
/// </summary>
internal sealed class IngestionWorker(
    IServiceScopeFactory scopes,
    IOptionsMonitor<IngestionOptions> options,
    IngestionTelemetry telemetry,
    TimeProvider time,
    ILogger<IngestionWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            using var generation = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            using (options.OnChange(_ => generation.Cancel()))
            {
                await RunAsync(options.CurrentValue, generation.Token, stoppingToken);
            }

            if (!stoppingToken.IsCancellationRequested)
            {
                logger.LogInformation("The source configuration changed; restarting the pull loops");
            }
        }
    }

    // Loops end when the generation ends; a pull already started runs to completion under the stopping token.
    private async Task RunAsync(IngestionOptions settings, CancellationToken generation, CancellationToken stoppingToken)
    {
        var pulls = new List<(int Priority, string Name, Func<IngestionCycle, IServiceProvider, Task<int>> Pull)>();
        foreach (MetricSeriesDefinition series in settings.MetricSeries)
        {
            pulls.Add((series.Priority, $"metrics:{series.Name}", (cycle, provider) => cycle.PullMetricsAsync(provider.GetRequiredService<IMetricsProvider>(), series, stoppingToken)));
        }

        pulls.Add((8, "deployments", (cycle, provider) => cycle.PullDeploymentsAsync(provider.GetRequiredService<IDeploymentProvider>(), stoppingToken)));
        foreach (LogSourceDefinition source in settings.LogSources)
        {
            pulls.Add((source.Priority, $"logs:{source.Name}", (cycle, provider) => cycle.PullLogsAsync(provider.GetRequiredService<ILogsProvider>(), source, stoppingToken)));
        }

        foreach (TraceSourceDefinition source in settings.TraceSources)
        {
            pulls.Add((0, $"traces:{source.Name}", (cycle, provider) => cycle.PullTracesAsync(provider.GetRequiredService<ITracesProvider>(), source, stoppingToken)));
        }

        if (settings.TopologyEnabled)
        {
            pulls.Add((-1, "topology", (cycle, provider) => cycle.PullTopologyAsync(provider.GetRequiredService<ITopologyProvider>(), stoppingToken)));
        }

        (int Priority, string Name, Func<IngestionCycle, IServiceProvider, Task<int>> Pull)[] ordered = [.. pulls.OrderByDescending(pull => pull.Priority)];
        await Task.WhenAll(ordered.Select(pull => LoopAsync(pull.Name, pull.Pull, settings.PollInterval, generation, stoppingToken)));
    }

    private async Task LoopAsync(string source, Func<IngestionCycle, IServiceProvider, Task<int>> pull, TimeSpan interval, CancellationToken generation, CancellationToken stoppingToken)
    {
        while (!generation.IsCancellationRequested)
        {
            await PullAsync(source, pull, stoppingToken);
            await Task.Delay(interval, time, generation).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        }
    }

    private async Task PullAsync(string source, Func<IngestionCycle, IServiceProvider, Task<int>> pull, CancellationToken stoppingToken)
    {
        long started = Stopwatch.GetTimestamp();
        try
        {
            await using AsyncServiceScope scope = scopes.CreateAsyncScope();
            int added = await pull(scope.ServiceProvider.GetRequiredService<IngestionCycle>(), scope.ServiceProvider);
            telemetry.Ingested.Add(added, new KeyValuePair<string, object?>("source", source));
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !stoppingToken.IsCancellationRequested)
        {
            telemetry.PullFailures.Add(1, new KeyValuePair<string, object?>("source", source));
            logger.LogError(exception, "Pulling {Source} failed; it is retried next cycle", source);
        }
        finally
        {
            telemetry.PullDuration.Record(Stopwatch.GetElapsedTime(started).TotalSeconds, new KeyValuePair<string, object?>("source", source));
        }
    }
}

/// <summary>
/// Ingestion's own telemetry for the Phase 02 operational dashboard: throughput, duplicates, late
/// data, watermark lag, failures and pull latency per source. The Aspire dashboard shows it locally.
/// </summary>
internal sealed class IngestionTelemetry : IIngestionMetrics, IDisposable
{
    public const string MeterName = "Sentinel.Ingestion";

    private readonly Meter _meter = new(MeterName);

    public IngestionTelemetry()
    {
        Ingested = _meter.CreateCounter<long>("sentinel.ingestion.observations", "{observation}", "New observations stored and published, per source.");
        PullFailures = _meter.CreateCounter<long>("sentinel.ingestion.pull.failures", "{failure}", "Pulls that threw, per source.");
        PullDuration = _meter.CreateHistogram("sentinel.ingestion.pull.duration", "s", "Time to pull and commit one source.", tags: null,
            new InstrumentAdvice<double> { HistogramBucketBoundaries = [0.01, 0.025, 0.05, 0.1, 0.25, 0.5, 1, 2.5, 5, 10] });
        Duplicates = _meter.CreateCounter<long>("sentinel.ingestion.duplicates", "{observation}", "Observations a pull returned that were already stored, per source.");
        Late = _meter.CreateCounter<long>("sentinel.ingestion.late", "{observation}", "New observations older than the source's watermark (ADR-016), per source.");
        WatermarkLag = _meter.CreateHistogram("sentinel.ingestion.watermark.lag", "s", "How far a source's watermark trails now after a pull.", tags: null,
            new InstrumentAdvice<double> { HistogramBucketBoundaries = [5, 10, 15, 30, 60, 120, 300, 600] });
        SourceFailures = _meter.CreateCounter<long>("sentinel.ingestion.source.failures", "{failure}", "Provider calls that returned an outage, per source.");
    }

    public Counter<long> Ingested { get; }

    public Counter<long> PullFailures { get; }

    public Histogram<double> PullDuration { get; }

    public Counter<long> Duplicates { get; }

    public Counter<long> Late { get; }

    public Histogram<double> WatermarkLag { get; }

    public Counter<long> SourceFailures { get; }

    public void Pulled(string sourceId, int added, int duplicates, int late, TimeSpan watermarkLag)
    {
        var source = new KeyValuePair<string, object?>("source", sourceId);
        Duplicates.Add(duplicates, source);
        Late.Add(late, source);
        WatermarkLag.Record(watermarkLag.TotalSeconds, source);
    }

    public void SourceFailed(string sourceId, bool unavailable) =>
        SourceFailures.Add(1, new KeyValuePair<string, object?>("source", sourceId), new KeyValuePair<string, object?>("unavailable", unavailable));

    public void Dispose() => _meter.Dispose();
}
