using CleanArchitecture.BuildingBlocks;
using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.Runtime.Domain.Incidents.Events;

/// <summary>
/// Runtime assigned a bounded investigation: a specialist <paramref name="Role"/> (Phase 04), or the
/// adversarial verifier with the hypotheses it must try to falsify (Phase 05), or the escalation investigator with the two
/// explanations to separate and how they stood (Phase E05). A null role is the generalist investigator of the walking skeleton.
/// </summary>
public sealed record InvestigationRequested(
    InvestigationId InvestigationId,
    string Objective,
    DateTimeOffset WindowFrom,
    DateTimeOffset WindowTo,
    DateTimeOffset OccurredAt,
    string? Role = null,
    IReadOnlyList<HypothesisId>? ChallengedHypothesisIds = null,
    IReadOnlyList<PriorIncident>? SimilarIncidents = null,
    EscalationState? Escalation = null) : IIncidentEvent;
