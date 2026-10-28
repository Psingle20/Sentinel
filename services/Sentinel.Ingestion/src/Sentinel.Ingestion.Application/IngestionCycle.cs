using CleanArchitecture.BuildingBlocks.Integration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sentinel.Contracts;
using Sentinel.Contracts.Messaging;
using Sentinel.Contracts.Observations;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Contracts.Queries;
using Sentinel.Ingestion.Application.Providers;
using Sentinel.Ingestion.Domain.Sources;

namespace Sentinel.Ingestion.Application;

/// <summary>
/// One pull from one source: read from the checkpoint, store new observations, publish them and
/// move the checkpoint, all in one unit of work. A crash before the commit repeats the pull;
/// deterministic IDs make the repeat a no-op for anything already stored (Phase 02).
/// </summary>
public sealed class IngestionCycle(
    IObservationStore store,
    ISourceStateStore states,
    IOutbox outbox,
    IUnitOfWork unitOfWork,
    TimeProvider time,
    IOptions<IngestionOptions> options,
    ILogger<IngestionCycle> logger,
    IIngestionMetrics? metrics = null)
{
    private static readonly TimeSpan DeploymentLag = TimeSpan.FromSeconds(1);

    private readonly IngestionOptions _options = options.Value;

    public static string MetricSourceId(string provider, string series) => $"{provider}:{series}";

    public static string DeploymentSourceId(string provider) => $"{provider}:deployments";

    public static string LogSourceId(string provider, string source) => $"{provider}:logs:{source}";

    public static string TraceSourceId(string provider, string source) => $"{provider}:traces:{source}";

    public static string TopologySourceId(string provider) => $"{provider}:topology";

    public async Task<int> PullMetricsAsync(IMetricsProvider provider, MetricSeriesDefinition series, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(series);

        DateTimeOffset now = time.GetUtcNow();
        TimeSpan step = _options.MetricStep;
        string sourceId = MetricSourceId(provider.Name, series.Name);
        SourceState state = await states.GetAsync(sourceId, cancellationToken)
            ?? SourceState.Start(sourceId, provider.Name, Align(now, step), _options.InitialLookback);
        if (state.IsCoolingDown(now))
        {
            return 0;
        }

        // Align to the step so every pull asks for the same sample times and IDs stay stable.
        DateTimeOffset from = Align(state.Checkpoint, step);
        DateTimeOffset to = Min(Align(now - _options.IngestLag, step), from + _options.MaxRangePerPull);
        if (to <= from)
        {
            return 0;
        }

        ProviderResult<MetricObservation> result = await provider.QueryRangeAsync(
            new MetricRangeRequest(Tenant, Environment, series, new TimeRange(from, to), step),
            cancellationToken);
        return await CommitAsync(state, result, to, now, cancellationToken);
    }

    public async Task<int> PullDeploymentsAsync(IDeploymentProvider provider, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(provider);

        DateTimeOffset now = time.GetUtcNow();
        string sourceId = DeploymentSourceId(provider.Name);
        SourceState state = await states.GetAsync(sourceId, cancellationToken)
            ?? SourceState.Start(sourceId, provider.Name, now, _options.InitialLookback);
        if (state.IsCoolingDown(now))
        {
            return 0;
        }

        DateTimeOffset to = Min(now - DeploymentLag, state.Checkpoint + _options.MaxRangePerPull);
        if (to <= state.Checkpoint)
        {
            return 0;
        }

        ProviderResult<DeploymentObservation> result = await provider.GetDeploymentsAsync(
            new DeploymentRequest(Tenant, Environment, new TimeRange(state.Checkpoint, to)),
            cancellationToken);
        return await CommitAsync(state, result, to, now, cancellationToken);
    }

    public async Task<int> PullLogsAsync(ILogsProvider provider, LogSourceDefinition source, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(source);

        DateTimeOffset now = time.GetUtcNow();
        string sourceId = LogSourceId(provider.Name, source.Name);
        SourceState state = await states.GetAsync(sourceId, cancellationToken)
            ?? SourceState.Start(sourceId, provider.Name, now, _options.InitialLookback);
        (DateTimeOffset from, DateTimeOffset to) = Range(state, now, _options.LogLag);
        if (to <= from || state.IsCoolingDown(now))
        {
            return 0;
        }

        ProviderResult<LogObservation> result = await provider.QueryRangeAsync(new LogRangeRequest(Tenant, Environment, source, new TimeRange(from, to)), cancellationToken);
        return await CommitAsync(state, result, to, now, cancellationToken);
    }

    public async Task<int> PullTracesAsync(ITracesProvider provider, TraceSourceDefinition source, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(source);

        DateTimeOffset now = time.GetUtcNow();
        string sourceId = TraceSourceId(provider.Name, source.Name);
        SourceState state = await states.GetAsync(sourceId, cancellationToken)
            ?? SourceState.Start(sourceId, provider.Name, now, _options.InitialLookback);
        (DateTimeOffset from, DateTimeOffset to) = Range(state, now, _options.TraceLag);
        if (to <= from || state.IsCoolingDown(now))
        {
            return 0;
        }

        ProviderResult<TraceObservation> result = await provider.QueryRangeAsync(new TraceRangeRequest(Tenant, Environment, source, new TimeRange(from, to)), cancellationToken);
        return await CommitAsync(state, result, to, now, cancellationToken);
    }

    /// <summary>Pulls the dependency graph at most once per <see cref="IngestionOptions.TopologyInterval"/>.</summary>
    public async Task<int> PullTopologyAsync(ITopologyProvider provider, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(provider);

        DateTimeOffset now = time.GetUtcNow();
        string sourceId = TopologySourceId(provider.Name);
        SourceState? state = await states.GetAsync(sourceId, cancellationToken);
        if (state is not null && (now - state.UpdatedAt < _options.TopologyInterval || state.IsCoolingDown(now)))
        {
            return 0;
        }

        state ??= SourceState.Start(sourceId, provider.Name, now, _options.TopologyLookback);
        DateTimeOffset to = now - _options.TraceLag;
        ProviderResult<TopologyObservation> result = await provider.GetDependenciesAsync(
            new TopologyRequest(Tenant, Environment, new TimeRange(to - _options.TopologyLookback, to)), cancellationToken);
        return await CommitAsync(state, result, to, now, cancellationToken);
    }

    // Logs and traces are indexed late, so each pull re-reads the last LateDataOverlap; deterministic
    // IDs make the overlap free of duplicates and anything new in it is marked late (ADR-016).
    private (DateTimeOffset From, DateTimeOffset To) Range(SourceState state, DateTimeOffset now, TimeSpan lag)
    {
        DateTimeOffset from = state.Checkpoint - _options.LateDataOverlap;
        return (from, Min(now - lag, from + _options.MaxRangePerPull));
    }

    private TenantId Tenant => TenantId.From(_options.Tenant);

    private EnvironmentId Environment => EnvironmentId.From(_options.Environment);

    private async Task<int> CommitAsync<TObservation>(
        SourceState state,
        ProviderResult<TObservation> result,
        DateTimeOffset through,
        DateTimeOffset now,
        CancellationToken cancellationToken)
        where TObservation : Observation
    {
        if (result.Status == ProviderStatus.Unavailable)
        {
            SourceState failed = state.Failed(result.Error ?? "The provider is unavailable.", now);
            await states.SaveAsync(failed, cancellationToken);
            await unitOfWork.CommitAsync(cancellationToken);
            logger.LogWarning("Source {Source} failed ({Failures} in a row): {Error}", state.SourceId, failed.ConsecutiveFailures, failed.LastError);
            metrics?.SourceFailed(state.SourceId, failed.Health == SourceHealth.Unavailable);
            return 0;
        }

        Observation[] marked = [.. result.Observations.Select(observation => observation.EventTime < state.Watermark ? MarkLate(observation) : observation)];
        IReadOnlyList<Observation> added = await store.AddNewAsync(marked, cancellationToken);
        IReadOnlyList<ObservationKind> published = _options.EffectivePublishKinds;
        foreach (Observation observation in added.Where(observation => published.Contains(KindOf(observation))))
        {
            outbox.Enqueue(
                new ObservationRecorded(observation),
                new MessageMetadata(Headers: SentinelHeaders.For(observation.TenantId, observation.EnvironmentId, observation.ServiceId)));
        }

        // A truncated pull is complete only up to its last observation; the next pull continues from there.
        DateTimeOffset completeThrough = result.Truncated && result.Observations.Count > 0
            ? result.Observations.Max(observation => observation.EventTime)
            : through;
        SourceState advanced = state.Advanced(completeThrough, now);
        await states.SaveAsync(advanced, cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
        metrics?.Pulled(
            state.SourceId,
            added.Count,
            marked.Length - added.Count,
            added.Count(observation => observation.Attributes.ContainsKey("late")),
            now - advanced.Watermark);
        return added.Count;
    }

    public static ObservationKind KindOf(Observation observation) => observation switch
    {
        MetricObservation => ObservationKind.Metric,
        LogObservation => ObservationKind.Log,
        TraceObservation => ObservationKind.Trace,
        DeploymentObservation => ObservationKind.Deployment,
        TopologyObservation => ObservationKind.Topology,
        _ => throw new ArgumentOutOfRangeException(nameof(observation), observation.GetType().Name, "Unknown observation type.")
    };

    private static Observation MarkLate(Observation observation) =>
        observation with { Attributes = new Dictionary<string, string>(observation.Attributes, StringComparer.Ordinal) { ["late"] = "true" } };

    private static DateTimeOffset Align(DateTimeOffset value, TimeSpan step) =>
        new(value.UtcTicks - value.UtcTicks % step.Ticks, TimeSpan.Zero);

    private static DateTimeOffset Min(DateTimeOffset first, DateTimeOffset second) => first < second ? first : second;
}
