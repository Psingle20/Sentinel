using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.Runtime.Domain.Incidents;

/// <summary>A hypothesis as the blackboard holds it, with where it came from.</summary>
public sealed record BlackboardHypothesis(
    Hypothesis Hypothesis,
    IReadOnlyList<HypothesisId> Lineage,
    IReadOnlyList<InvestigationId> ProposedBy,
    HypothesisId? Refines,
    bool Subsumed);
