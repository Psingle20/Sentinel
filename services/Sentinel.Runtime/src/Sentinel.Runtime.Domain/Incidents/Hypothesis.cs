using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.Runtime.Domain.Incidents;

public sealed record Hypothesis
{
    public required HypothesisId HypothesisId { get; init; }

    public required string Statement { get; init; }

    public required IReadOnlyList<string> CausalChain { get; init; }

    public ServiceId? SuspectedServiceId { get; init; }

    public ObservationId? SuspectedChangeObservationId { get; init; }

    public IReadOnlyList<EvidenceId> SupportingEvidenceIds { get; init; } = [];

    public IReadOnlyList<EvidenceId> ContradictingEvidenceIds { get; init; } = [];

    /// <summary>The explanation's identity on the blackboard; equal keys merge, and <c>a:b</c> refines <c>a</c>.</summary>
    public string? Key { get; init; }
}
