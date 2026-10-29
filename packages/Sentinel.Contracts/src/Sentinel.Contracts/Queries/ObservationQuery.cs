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
