using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.Runtime.Domain.Incidents;

public sealed record Evidence
{
    public required EvidenceId EvidenceId { get; init; }

    public required string Statement { get; init; }

    public required EvidenceDirection Direction { get; init; }

    public required double Strength { get; init; }

    public required IReadOnlyList<ObservationId> SourceObservationIds { get; init; }

    public IReadOnlyList<HypothesisId> HypothesisIds { get; init; } = [];

    public required DateTimeOffset From { get; init; }

    public required DateTimeOffset To { get; init; }

    public required string ToolCallId { get; init; }

    public string? IndependenceGroup { get; init; }

    /// <summary>Derived from an incomplete, truncated or stale query result (ADR-007); weighted down by consensus.</summary>
    public bool Partial { get; init; }
}
