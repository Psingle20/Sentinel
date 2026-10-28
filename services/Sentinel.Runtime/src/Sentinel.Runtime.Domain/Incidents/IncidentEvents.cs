using CleanArchitecture.BuildingBlocks;
using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.Runtime.Domain.Incidents;

/// <summary>
/// An event in an incident's stream. The stream is the authoritative record of the incident
/// (ADR-004): its state is rebuilt by applying these events in version order.
/// </summary>
public interface IIncidentEvent : IDomainEvent
{
    DateTimeOffset OccurredAt { get; }
}

public sealed record IncidentDetected(
    IncidentId IncidentId,
    TenantId TenantId,
    EnvironmentId EnvironmentId,
    ServiceId ServiceId,
    string Title,
    IncidentSeverity Severity,
    DateTimeOffset SymptomOnset,
    IReadOnlyList<ObservationId> TriggerObservationIds,
    IReadOnlyList<LinkedChange> LinkedChanges,
    DateTimeOffset OccurredAt,
    string? TriggerMetric = null) : IIncidentEvent;

/// <summary>
/// Runtime assigned a bounded investigation: a specialist <paramref name="Role"/> (Phase 04), or the
/// adversarial verifier with the hypotheses it must try to falsify (Phase 05). A null role is the
/// generalist investigator of the walking skeleton.
/// </summary>
public sealed record InvestigationRequested(
    InvestigationId InvestigationId,
    string Objective,
    DateTimeOffset WindowFrom,
    DateTimeOffset WindowTo,
    DateTimeOffset OccurredAt,
    string? Role = null,
    IReadOnlyList<HypothesisId>? ChallengedHypothesisIds = null,
    IReadOnlyList<PriorIncident>? SimilarIncidents = null) : IIncidentEvent;

/// <summary>An investigation did not report within its budget; the incident proceeds without it.</summary>
public sealed record InvestigationTimedOut(InvestigationId InvestigationId, DateTimeOffset OccurredAt) : IIncidentEvent;

public sealed record InvestigationReported(
    InvestigationReport Report,
    IReadOnlyList<RejectedEvidence> RejectedEvidence,
    DateTimeOffset OccurredAt) : IIncidentEvent;

/// <summary>A change learned after detection, such as a deployment completing, that bears on the incident.</summary>
public sealed record ChangeUpdated(LinkedChange Change, DateTimeOffset OccurredAt) : IIncidentEvent;

public sealed record DecisionMade(Decision Decision, DateTimeOffset OccurredAt) : IIncidentEvent;

/// <summary>A later observation superseded the current decision; it stays in the history (Phase 05).</summary>
public sealed record DecisionSuperseded(DecisionId DecisionId, DecisionId SupersededBy, string Reason, DateTimeOffset OccurredAt) : IIncidentEvent;

/// <summary>
/// Correlation merged a related service's symptom into the incident (Phase 03): the blast radius grew.
/// The score and reasons explain why it was merged rather than opened as its own incident.
/// </summary>
public sealed record SignalLinked(
    ServiceId ServiceId,
    string MetricName,
    double Value,
    IReadOnlyList<ObservationId> ObservationIds,
    double Score,
    IReadOnlyList<string> Reasons,
    DateTimeOffset OccurredAt) : IIncidentEvent;

public sealed record SeverityChanged(IncidentSeverity Severity, string Reason, DateTimeOffset OccurredAt) : IIncidentEvent;

/// <summary>
/// The same symptom returned soon after the incident was resolved, with no new change to explain
/// it: the incident reopens and its investigation cycle starts again. Earlier cycles stay in the stream.
/// </summary>
public sealed record IncidentReopened(string Reason, IReadOnlyList<ObservationId> TriggerObservationIds, DateTimeOffset SymptomOnset, DateTimeOffset OccurredAt) : IIncidentEvent;

public sealed record PlanProposed(ProposedPlan Plan, DateTimeOffset OccurredAt) : IIncidentEvent;

public sealed record PlanApproved(PlanId PlanId, int PlanVersion, string PlanHash, string ApproverId, string Attestation, DateTimeOffset OccurredAt) : IIncidentEvent;

public sealed record PlanRejected(PlanId PlanId, int PlanVersion, string RejectedBy, string Reason, DateTimeOffset OccurredAt) : IIncidentEvent;

/// <summary>An approver withdrew an approval before execution started (Phase 06); no start can be confirmed for it.</summary>
public sealed record ApprovalRevoked(PlanId PlanId, int PlanVersion, string RevokedBy, string Reason, DateTimeOffset OccurredAt) : IIncidentEvent;

/// <summary>
/// Runtime asked Executor to run the approved plan. The version of this event in the stream is
/// the fencing token (ADR-004, ADR-006).
/// </summary>
public sealed record ExecutionRequested(PlanId PlanId, int PlanVersion, long FencingToken, string IdempotencyKey, DateTimeOffset OccurredAt) : IIncidentEvent;

/// <summary>The point of no return: Runtime confirmed Executor may cause side effects.</summary>
public sealed record ExecutionStarted(PlanId PlanId, int PlanVersion, long FencingToken, DateTimeOffset OccurredAt) : IIncidentEvent;

public sealed record ExecutionReported(PlanId PlanId, int PlanVersion, ExecutionResult Result, string? Detail, DateTimeOffset OccurredAt) : IIncidentEvent;

public sealed record RecoveryScheduled(PlanId PlanId, DateTimeOffset WindowFrom, DateTimeOffset WindowTo, DateTimeOffset OccurredAt) : IIncidentEvent;

public sealed record RecoveryEvaluated(
    PlanId PlanId,
    RecoveryResult Result,
    IReadOnlyList<Measurement> Measurements,
    DateTimeOffset ObservedThrough,
    string? Reason,
    DateTimeOffset OccurredAt) : IIncidentEvent;

public sealed record IncidentResolved(string Reason, DateTimeOffset OccurredAt) : IIncidentEvent;

public sealed record IncidentEscalated(string Reason, DateTimeOffset OccurredAt) : IIncidentEvent;
