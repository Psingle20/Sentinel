using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Configuration;
using Sentinel.Ingestion.Application.Options;

namespace Sentinel.Ingestion.Application.ControlPlane;

/// <summary>
/// Ingestion's control-plane document (Phase 08 provider configuration): which metric series, log
/// streams and trace sources are pulled, how often, and how far back. Provider endpoints and
/// credentials stay deployment configuration and are never part of it (ADR-007), and so do the
/// tenant, the environment and the query bulkhead. A published version restarts the pull loops
/// with the new sources once their current pulls finish.
/// </summary>
public sealed record SourceConfigurationDocument : IVersionedDocument
{
    public required string Version { get; init; }

    public TimeSpan PollInterval { get; init; } = TimeSpan.FromSeconds(5);

    public TimeSpan MetricStep { get; init; } = TimeSpan.FromSeconds(5);

    public TimeSpan InitialLookback { get; init; } = TimeSpan.FromMinutes(2);

    public TimeSpan MaxRangePerPull { get; init; } = TimeSpan.FromMinutes(5);

    public TimeSpan LogLag { get; init; } = TimeSpan.FromSeconds(30);

    public TimeSpan TraceLag { get; init; } = TimeSpan.FromSeconds(30);

    public TimeSpan LateDataOverlap { get; init; } = TimeSpan.FromSeconds(30);

    public bool TopologyEnabled { get; init; }

    public TimeSpan TopologyInterval { get; init; } = TimeSpan.FromMinutes(1);

    public TimeSpan TopologyLookback { get; init; } = TimeSpan.FromMinutes(15);

    public IReadOnlyList<MetricSeriesDefinition> MetricSeries { get; init; } = [];

    public IReadOnlyList<LogSourceDefinition> LogSources { get; init; } = [];

    public IReadOnlyList<TraceSourceDefinition> TraceSources { get; init; } = [];
}
