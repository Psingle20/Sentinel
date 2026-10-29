using System.Text.Json.Serialization;
using Sentinel.Contracts.Messaging;
using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.Contracts.Investigations;

public sealed record HypothesisFinding
{
    public required HypothesisId HypothesisId { get; init; }

    public required string Statement { get; init; }

    /// <summary>The proposed chain from cause to symptom, in order.</summary>
    public required IReadOnlyList<string> CausalChain { get; init; }

    public ServiceId? SuspectedServiceId { get; init; }

    public ObservationId? SuspectedChangeObservationId { get; init; }

    public IReadOnlyList<EvidenceId> SupportingEvidenceIds { get; init; } = [];

    public IReadOnlyList<EvidenceId> ContradictingEvidenceIds { get; init; } = [];

    /// <summary>
    /// The explanation's stable identity on the blackboard (Phase 04): investigators that propose the
    /// same key propose the same explanation, and Runtime merges them. A key with a <c>:</c> refines
    /// its prefix, for example <c>redis-saturation:reporting-worker</c> refines <c>redis-saturation</c>.
    /// </summary>
    public string? Key { get; init; }
}
