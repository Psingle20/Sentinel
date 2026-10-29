using Sentinel.Contracts.Observations;
using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.Contracts.Queries;

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
