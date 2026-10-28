using Sentinel.Contracts.Observations;
using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.Contracts.Queries;

/// <summary>A read-only, tenant-scoped query served by Sentinel.Ingestion (ADR-007).</summary>
public sealed record ObservationQuery
{
    public required TenantId TenantId { get; init; }

    public required EnvironmentId EnvironmentId { get; init; }

    public ServiceId? ServiceId { get; init; }

    public required ObservationKind Kind { get; init; }

    public required TimeRange Range { get; init; }

    /// <summary>Kind-specific filters, for example <c>metricName</c> or <c>minimumSeverity</c>.</summary>
    public IReadOnlyDictionary<string, string> Filters { get; init; } = new Dictionary<string, string>();

    public int Limit { get; init; } = 500;

    public string? ContinuationToken { get; init; }
}

public enum QueryCompleteness
{
    Complete,
    Partial
}

public enum ProviderStatus
{
    Healthy,
    Degraded,
    Unavailable
}

public sealed record ProviderHealth(string Provider, ProviderStatus Status);

/// <summary>
/// What a result can be trusted for. Recovery is never verified from a result that is
/// incomplete, truncated or not observed through the whole window (ADR-007).
/// </summary>
public sealed record QueryMetadata
{
    /// <summary>Event time up to which the result is complete: the watermark.</summary>
    public required DateTimeOffset ObservedThrough { get; init; }

    public required TimeSpan Freshness { get; init; }

    public required QueryCompleteness Completeness { get; init; }

    public required bool Truncated { get; init; }

    public required IReadOnlyList<ProviderHealth> ProviderHealth { get; init; }

    public required string RedactionPolicyVersion { get; init; }

    public string? ContinuationToken { get; init; }
}

public sealed record ObservationQueryResult(IReadOnlyList<Observation> Observations, QueryMetadata Metadata);
