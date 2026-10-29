using System.Text.Json.Serialization;
using Sentinel.Contracts.Messaging;
using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.Contracts.Investigations;

/// <summary>
/// A claim backed by specific observations and the tool call that produced it. Runtime decides
/// whether it becomes accepted evidence (architecture §15).
/// </summary>
public sealed record EvidenceFinding
{
    public required EvidenceId EvidenceId { get; init; }

    public required string Statement { get; init; }

    public required EvidenceDirection Direction { get; init; }

    /// <summary>Strength from 0 to 1.</summary>
    public required double Strength { get; init; }

    public required IReadOnlyList<ObservationId> SourceObservationIds { get; init; }

    public IReadOnlyList<HypothesisId> HypothesisIds { get; init; } = [];

    public required TimeRange EventTimeRange { get; init; }

    public required string ToolCallId { get; init; }

    /// <summary>Findings derived from the same underlying signal share a group, so they are counted once (Phase 05).</summary>
    public string? IndependenceGroup { get; init; }

    /// <summary>
    /// True when the query behind the evidence was incomplete, truncated or not observed through its
    /// range (ADR-007). Written only when true, so complete evidence keeps its schema-1 wire form.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool Partial { get; init; }
}
