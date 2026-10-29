using Sentinel.Contracts.Observations;

namespace Sentinel.Ingestion.Application.Options;

/// <summary>Configured as <c>Ingestion</c>.</summary>
public sealed class IngestionOptions
{
    public const string Section = "Ingestion";

    /// <summary>The tenant and environment this deployment ingests for. Multi-tenancy beyond identifiers is Phase 10.</summary>
    public string Tenant { get; set; } = string.Empty;

    public string Environment { get; set; } = string.Empty;

    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>How far behind now a pull ends, so the provider has the data in hand.</summary>
    public TimeSpan IngestLag { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>Where a source seen for the first time starts.</summary>
    public TimeSpan InitialLookback { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>The largest range one pull covers; a source that fell behind catches up over several cycles.</summary>
    public TimeSpan MaxRangePerPull { get; set; } = TimeSpan.FromMinutes(5);

    public TimeSpan MetricStep { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>Allowed lateness for logs and traces, which their backends index asynchronously (ADR-016).</summary>
    public TimeSpan LogLag { get; set; } = TimeSpan.FromSeconds(30);

    public TimeSpan TraceLag { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>How much of the previous range a log or trace pull reads again to catch late data.</summary>
    public TimeSpan LateDataOverlap { get; set; } = TimeSpan.FromSeconds(30);

    public TimeSpan TopologyInterval { get; set; } = TimeSpan.FromMinutes(1);

    public TimeSpan TopologyLookback { get; set; } = TimeSpan.FromMinutes(15);

    public bool TopologyEnabled { get; set; }

    /// <summary>Queries served at once; the bulkhead that keeps an investigation burst from starving ingestion (Phase 02).</summary>
    public int QueryConcurrency { get; set; } = 16;

    /// <summary>Queries allowed to wait for a slot; beyond this they are refused at once rather than queued in memory.</summary>
    public int QueryQueueLimit { get; set; } = 64;

    public TimeSpan QueryQueueTimeout { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Observation kinds published as <c>ObservationRecorded</c> events. High-volume logs and traces
    /// have no event consumer: they are stored and reached through the query contract, which keeps
    /// the event transport for signals that drive correlation (ADR-008). Empty means the default.
    /// </summary>
    public IList<ObservationKind> PublishKinds { get; } = [];

    public static readonly IReadOnlyList<ObservationKind> DefaultPublishKinds = [ObservationKind.Metric, ObservationKind.Deployment, ObservationKind.Topology];

    public IReadOnlyList<ObservationKind> EffectivePublishKinds => PublishKinds.Count == 0 ? DefaultPublishKinds : [.. PublishKinds];

    public IList<MetricSeriesDefinition> MetricSeries { get; } = [];

    public IList<LogSourceDefinition> LogSources { get; } = [];

    public IList<TraceSourceDefinition> TraceSources { get; } = [];

    public bool IsValid() =>
        !string.IsNullOrWhiteSpace(Tenant)
        && !string.IsNullOrWhiteSpace(Environment)
        && PollInterval > TimeSpan.Zero
        && IngestLag >= TimeSpan.Zero
        && InitialLookback > TimeSpan.Zero
        && MaxRangePerPull > MetricStep
        && MetricStep > TimeSpan.Zero
        && QueryConcurrency > 0
        && QueryQueueLimit >= 0
        && MetricSeries.All(series => !string.IsNullOrWhiteSpace(series.Name)
            && !string.IsNullOrWhiteSpace(series.Expression)
            && (series.Service is not null || series.ServiceLabel is not null));
}
