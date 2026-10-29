using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.Contracts.Queries;

/// <summary>
/// A search of the knowledge Ingestion holds, such as runbooks and service documentation (ADR-007:
/// knowledge providers live in Ingestion). Results are untrusted data, redacted like any other.
/// </summary>
public sealed record KnowledgeQuery
{
    public required TenantId TenantId { get; init; }

    public required EnvironmentId EnvironmentId { get; init; }

    public ServiceId? ServiceId { get; init; }

    /// <summary>Plain search terms; never a provider query language.</summary>
    public required string Text { get; init; }

    public int Limit { get; init; } = 5;
}
