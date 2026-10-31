using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sentinel.Ingestion.Application.Abstractions.Providers;
using Sentinel.Ingestion.Application.Options;
using Sentinel.Ingestion.Application.Services;
using Sentinel.Ingestion.Infrastructure.Diagnostics;
using Sentinel.Ingestion.Infrastructure.Providers;

namespace Sentinel.Ingestion.Infrastructure.Workers;

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
        // The AWS environment adds a second source of metrics, deployments and topology (Phase E01).
        string[] metricSources;
        string[] deploymentSources;
        string[] topologySources;
        await using (AsyncServiceScope scope = scopes.CreateAsyncScope())
        {
            metricSources = [.. scope.ServiceProvider.GetServices<IMetricsProvider>().Select(provider => provider.Name)];
            deploymentSources = [.. scope.ServiceProvider.GetServices<IDeploymentProvider>().Select(provider => provider.Name)];
            topologySources = [.. scope.ServiceProvider.GetServices<ITopologyProvider>().Select(provider => provider.Name)];
        }

        var pulls = new List<(int Priority, string Name, Func<IngestionCycle, IServiceProvider, Task<int>> Pull)>();
        foreach (MetricSeriesDefinition series in settings.MetricSeries)
        {
            // Each series is pulled from the provider it names; a provider that is not configured is said so once, not
            // retried every cycle.
            string source = series.Provider ?? PrometheusMetricsProvider.ProviderName;
            if (!metricSources.Contains(source, StringComparer.Ordinal))
            {
                logger.LogWarning("Metric series {Series} names provider {Provider}, which is not configured; it is not pulled", series.Name, source);
                continue;
            }

            pulls.Add((series.Priority, $"metrics:{series.Name}", (cycle, provider) => cycle.PullMetricsAsync(provider.GetServices<IMetricsProvider>().Single(candidate => candidate.Name == source), series, stoppingToken)));
        }

        foreach (string source in deploymentSources)
        {
            string name = deploymentSources.Length == 1 ? "deployments" : $"deployments:{source}";
            pulls.Add((8, name, (cycle, provider) => cycle.PullDeploymentsAsync(provider.GetServices<IDeploymentProvider>().Single(candidate => candidate.Name == source), stoppingToken)));
        }
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
            foreach (string source in topologySources)
            {
                string name = topologySources.Length == 1 ? "topology" : $"topology:{source}";
                pulls.Add((-1, name, (cycle, provider) => cycle.PullTopologyAsync(provider.GetServices<ITopologyProvider>().Single(candidate => candidate.Name == source), stoppingToken)));
            }
        }

        if (settings.AlertsEnabled)
        {
            pulls.Add((0, "alerts", (cycle, provider) => cycle.PullAlertsAsync(provider.GetRequiredService<IAlertProvider>(), stoppingToken)));
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
