namespace Sentinel.Runtime.Domain.Incidents;

/// <summary>
/// How the surviving explanations stood when consensus abstained for ambiguity (Phase E05): what the escalation
/// investigator reasons over. Kept in the request so a replay sees the same input.
/// </summary>
public sealed record EscalationState(IReadOnlyList<HypothesisAssessment> Standing, double EvidenceCompleteness, string Rationale);
