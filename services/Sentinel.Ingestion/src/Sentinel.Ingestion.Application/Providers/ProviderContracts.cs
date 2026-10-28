using Sentinel.Contracts;
using Sentinel.Contracts.Observations;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Contracts.Queries;

namespace Sentinel.Ingestion.Application.Providers;

/// <summary>
/// One metric series to ingest. <see cref="Expression"/> is the provider's own query language and
/// stays in configuration; models never write it (ADR-007). The series' service is either fixed
/// (<see cref="Service"/>) or read from a label on each returned series (<see cref="ServiceLabel"/>).
/// </summary>
public sealed record MetricSeriesDefinition
{
    public required string Name { get; init; }

    public required string Expression { get; init; }

    public string? Service { get; init; }

    public string? ServiceLabel { get; init; }

    public string? Unit { get; init; }

    public string? Aggregation { get; init; }

    public TimeSpan? Window { get; init; }

    /// <summary>Higher-priority series are pulled first in each cycle (Phase 02 stream prioritization).</summary>
    public int Priority { get; init; }
}

public sealed record MetricRangeRequest(TenantId TenantId, EnvironmentId EnvironmentId, MetricSeriesDefinition Series, TimeRange Range, TimeSpan Step);

public sealed record DeploymentRequest(TenantId TenantId, EnvironmentId EnvironmentId, TimeRange Range);

/// <summary>
/// What a provider returned (ADR-014): canonical observations, the event time up to which they
/// are complete, whether limits truncated them and the provider's health. An outage is a
/// result with <see cref="ProviderStatus.Unavailable"/>, never an exception or an empty success.
/// </summary>
public sealed record ProviderResult<TObservation>(
    IReadOnlyList<TObservation> Observations,
    DateTimeOffset Watermark,
    bool Truncated,
    ProviderStatus Status,
    string? Error = null)
    where TObservation : Observation
{
    public static ProviderResult<TObservation> Unavailable(DateTimeOffset previousWatermark, string error) =>
        new([], previousWatermark, false, ProviderStatus.Unavailable, error);
}

public interface IMetricsProvider
{
    string Name { get; }

    Task<ProviderResult<MetricObservation>> QueryRangeAsync(MetricRangeRequest request, CancellationToken cancellationToken);
}

public interface IDeploymentProvider
{
    string Name { get; }

    Task<ProviderResult<DeploymentObservation>> GetDeploymentsAsync(DeploymentRequest request, CancellationToken cancellationToken);
}
