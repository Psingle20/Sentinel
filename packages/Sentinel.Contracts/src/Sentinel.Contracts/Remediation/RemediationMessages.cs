using System.Text.Json.Serialization;
using Sentinel.Contracts.Incidents;
using Sentinel.Contracts.Messaging;
using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.Contracts.Remediation;

public enum RiskLevel
{
    Low,
    Medium,
    High
}

/// <summary>A typed remediation action. Free text can never name an executable capability (Phase 04).</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(RollbackAction), "rollback")]
[JsonDerivedType(typeof(PauseBatchAction), "pause-batch")]
public abstract record RemediationAction
{
    public required ActionId ActionId { get; init; }

    public required ServiceId TargetServiceId { get; init; }
}

/// <summary>Roll a service back from its current revision to a previous one.</summary>
public sealed record RollbackAction : RemediationAction
{
    public required string FromRevision { get; init; }

    public required string ToRevision { get; init; }
}

/// <summary>Pause a service's background batch work; compensated by resuming it (Phase 06).</summary>
public sealed record PauseBatchAction : RemediationAction
{
    /// <summary>Why the batch is paused, for the operator who must resume it.</summary>
    public required string Reason { get; init; }
}

/// <summary>A condition the target must meet immediately before an action runs, checked through <c>Inspect</c>.</summary>
public sealed record PlanPrecondition(ServiceId ServiceId, string Condition, string Expected);

/// <summary>
/// An immutable plan version. Approval binds to <see cref="PlanHash"/>, so any change needs a
/// new version and a new approval (ADR-006).
/// </summary>
public sealed record RemediationPlan
{
    public required PlanId PlanId { get; init; }

    public required int PlanVersion { get; init; }

    public required string PlanHash { get; init; }

    public required HypothesisId HypothesisId { get; init; }

    public required IReadOnlyList<RemediationAction> Actions { get; init; }

    public required IReadOnlyList<RecoveryCriterion> ExpectedRecovery { get; init; }

    /// <summary>How long to observe after the action before judging recovery.</summary>
    public required TimeSpan ObservationWindow { get; init; }

    public required RiskLevel Risk { get; init; }

    /// <summary>Conditions Executor re-checks at the target just before acting (Phase 06).</summary>
    public IReadOnlyList<PlanPrecondition>? Preconditions { get; init; }

    /// <summary>What the action should change at the target, in operator terms.</summary>
    public string? ExpectedEffect { get; init; }

    /// <summary>Every service the plan's actions can affect: its blast radius.</summary>
    public IReadOnlyList<ServiceId>? BlastRadius { get; init; }

    /// <summary>How a partly applied plan is undone, or why it cannot be undone automatically.</summary>
    public string? Compensation { get; init; }
}

public enum PolicyDecision
{
    Allow,
    Deny,
    ApprovalRequired,

    /// <summary>The action may be suggested to responders but Sentinel will not execute it.</summary>
    SuggestionOnly
}


public sealed record PolicyEvaluation(PolicyDecision Decision, string PolicyVersion, IReadOnlyList<string> RuleTrace, DateTimeOffset EvaluatedAt);

/// <summary>Runtime proposed a plan, with Executor's advisory plan-time policy evaluation (ADR-006).</summary>
public sealed record RemediationPlanProposed : ISentinelMessage
{
    public static string MessageType => "runtime.remediation-plan.proposed";

    public static int SchemaVersion => 1;

    public static string Stream => SentinelStreams.Incidents;

    public required TenantId TenantId { get; init; }

    public required EnvironmentId EnvironmentId { get; init; }

    public required IncidentId IncidentId { get; init; }

    public required RemediationPlan Plan { get; init; }

    public required PolicyEvaluation PolicyEvaluation { get; init; }

    public required long StreamVersion { get; init; }
}

/// <summary>Api relays an approval with its signed attestation; Runtime cannot create one (ADR-006).</summary>
public sealed record RecordApproval : ISentinelMessage
{
    public static string MessageType => "runtime.record-approval";

    public static int SchemaVersion => 1;

    public static string Stream => SentinelStreams.RuntimeCommands;

    public required TenantId TenantId { get; init; }

    public required EnvironmentId EnvironmentId { get; init; }

    public required IncidentId IncidentId { get; init; }

    public required PlanId PlanId { get; init; }

    public required int PlanVersion { get; init; }

    public required string PlanHash { get; init; }

    /// <summary>Compact JWS signed by Api's approval key.</summary>
    public required string Attestation { get; init; }

    public required string ApproverId { get; init; }

    public required DateTimeOffset ApprovedAt { get; init; }
}

public sealed record RecordRejection : ISentinelMessage
{
    public static string MessageType => "runtime.record-rejection";

    public static int SchemaVersion => 1;

    public static string Stream => SentinelStreams.RuntimeCommands;

    public required TenantId TenantId { get; init; }

    public required EnvironmentId EnvironmentId { get; init; }

    public required IncidentId IncidentId { get; init; }

    public required PlanId PlanId { get; init; }

    public required int PlanVersion { get; init; }

    public required string RejectedBy { get; init; }

    public required string Reason { get; init; }

    public required DateTimeOffset RejectedAt { get; init; }
}

/// <summary>
/// Api relays an approver's revocation of an approval to Runtime (Phase 06). Runtime refuses the
/// start of any execution of the revoked plan, so a revoked attestation can never take effect.
/// </summary>
public sealed record RecordRevocation : ISentinelMessage
{
    public static string MessageType => "runtime.record-revocation";

    public static int SchemaVersion => 1;

    public static string Stream => SentinelStreams.RuntimeCommands;

    public required TenantId TenantId { get; init; }

    public required EnvironmentId EnvironmentId { get; init; }

    public required IncidentId IncidentId { get; init; }

    public required PlanId PlanId { get; init; }

    public required int PlanVersion { get; init; }

    public required string RevokedBy { get; init; }

    public required string Reason { get; init; }

    public required DateTimeOffset RevokedAt { get; init; }
}
/// <summary>
/// Runtime asks Executor to run an approved plan. <see cref="FencingToken"/> is the incident
/// stream version the command was based on (ADR-004, ADR-006).
/// </summary>
public sealed record ExecuteRemediation : ISentinelMessage
{
    public static string MessageType => "executor.execute-remediation";

    public static int SchemaVersion => 1;

    public static string Stream => SentinelStreams.ExecutorCommands;

    public required TenantId TenantId { get; init; }

    public required EnvironmentId EnvironmentId { get; init; }

    public required IncidentId IncidentId { get; init; }

    public required RemediationPlan Plan { get; init; }

    public required string Attestation { get; init; }

    public required long FencingToken { get; init; }

    public required string IdempotencyKey { get; init; }

    public required DateTimeOffset RequestedAt { get; init; }
}

/// <summary>Terminal states of Executor's execution state machine (ADR-006).</summary>
public enum ExecutionOutcome
{
    Rejected,
    Aborted,
    ActionVerified,
    Failed,
    OutcomeUnknown,
    Compensated,
    CompensationFailed
}

public sealed record RemediationExecuted : ISentinelMessage
{
    public static string MessageType => "executor.remediation.executed";

    public static int SchemaVersion => 1;

    public static string Stream => SentinelStreams.Remediation;

    public required TenantId TenantId { get; init; }

    public required EnvironmentId EnvironmentId { get; init; }

    public required IncidentId IncidentId { get; init; }

    public required PlanId PlanId { get; init; }

    public required int PlanVersion { get; init; }

    public required ExecutionOutcome Outcome { get; init; }

    public string? Detail { get; init; }

    public required DateTimeOffset CompletedAt { get; init; }
}

/// <summary>Synchronous plan-time policy evaluation, Runtime to Executor (ADR-006).</summary>
/// <summary>
/// What policy decides on (Phase 06): the plan and the incident facts it is conditioned on. Never
/// free text from a model. Confidence and severity come from Runtime's decision record.
/// </summary>
public sealed record PolicyEvaluationRequest(
    TenantId TenantId,
    EnvironmentId EnvironmentId,
    IncidentId IncidentId,
    RemediationPlan Plan,
    double? Confidence = null,
    Severity? Severity = null);

/// <summary>Synchronous start confirmation, Executor to Runtime, immediately before any side effect (ADR-006).</summary>
public sealed record RemediationStartRequest(TenantId TenantId, EnvironmentId EnvironmentId, IncidentId IncidentId, PlanId PlanId, int PlanVersion, long FencingToken);

public sealed record RemediationStartResponse(bool Confirmed, string? Reason);
