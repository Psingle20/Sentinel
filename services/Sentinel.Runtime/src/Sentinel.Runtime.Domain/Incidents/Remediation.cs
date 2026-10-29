using CleanArchitecture.BuildingBlocks;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Runtime.Domain.Incidents.Events;

namespace Sentinel.Runtime.Domain.Incidents;

/// <summary>
/// The incident's remediation (ADR-006), a child of <see cref="Incident"/>: the current plan, its
/// approval, rejection or revocation, and the execution Executor runs under a fencing token. Each
/// operation returns the events it decides; the incident appends them.
/// </summary>
internal sealed class Remediation
{
    private static readonly Dictionary<RemediationState, RemediationState[]> Transitions = new()
    {
        [RemediationState.NoPlan] = [RemediationState.Proposed],
        [RemediationState.Proposed] = [RemediationState.Proposed, RemediationState.Approved, RemediationState.Rejected],
        [RemediationState.Approved] = [RemediationState.Proposed, RemediationState.Rejected, RemediationState.Revoked, RemediationState.ExecutionRequested],
        [RemediationState.Rejected] = [RemediationState.Proposed],
        [RemediationState.Revoked] = [RemediationState.Proposed],
        [RemediationState.ExecutionRequested] = [RemediationState.Executing, RemediationState.Revoked, RemediationState.OutcomeUnknown, RemediationState.Finished],
        [RemediationState.Executing] = [RemediationState.OutcomeUnknown, RemediationState.Finished],
        // An approval can be withdrawn for as long as execution never started, even after Executor refused the plan.
        [RemediationState.OutcomeUnknown] = [RemediationState.OutcomeUnknown, RemediationState.Finished, RemediationState.Revoked],
        [RemediationState.Finished] = [RemediationState.Revoked]
    };

    public ProposedPlan? Plan { get; private set; }

    public PlanApproved? Approval { get; private set; }

    public PlanRejected? Rejection { get; private set; }

    public ApprovalRevoked? Revocation { get; private set; }

    public ExecutionRequested? Execution { get; private set; }

    public ExecutionStarted? ExecutionStart { get; private set; }

    public ExecutionReported? ExecutionOutcome { get; private set; }

    public RemediationState State => this switch
    {
        { ExecutionOutcome.Result: not ExecutionResult.OutcomeUnknown } => RemediationState.Finished,
        { ExecutionOutcome: not null } => RemediationState.OutcomeUnknown,
        { ExecutionStart: not null } => RemediationState.Executing,
        { Execution: not null } => RemediationState.ExecutionRequested,
        { Approval: not null } => RemediationState.Approved,
        { Plan: null } => RemediationState.NoPlan,
        { Plan: { } plan, Rejection: { } rejected } when rejected.PlanId == plan.PlanId && rejected.PlanVersion == plan.Version => RemediationState.Rejected,
        { Plan: { } plan, Revocation: { } revoked } when revoked.PlanId == plan.PlanId && revoked.PlanVersion == plan.Version => RemediationState.Revoked,
        _ => RemediationState.Proposed
    };

    /// <summary>A plan for the leading hypothesis of a diagnosis; only a newer version replaces the current one.</summary>
    public Result<IReadOnlyList<IIncidentEvent>> Propose(ProposedPlan plan, Decision? decision, IncidentState incident)
    {
        if (Plan is { } current && current.PlanId == plan.PlanId && current.Version == plan.Version)
        {
            return current.Hash == plan.Hash ? Unchanged() : Result.Failure<IReadOnlyList<IIncidentEvent>>(IncidentErrors.StalePlanVersion);
        }

        if (decision is not { Result: DecisionResult.Diagnosed } diagnosed || diagnosed.LeadingHypothesisId != plan.HypothesisId)
        {
            return Result.Failure<IReadOnlyList<IIncidentEvent>>(IncidentErrors.PlanWithoutDiagnosis);
        }

        if (Plan is not null && plan.Version <= Plan.Version)
        {
            return Result.Failure<IReadOnlyList<IIncidentEvent>>(IncidentErrors.StalePlanVersion);
        }

        if (!CanBecome(RemediationState.Proposed))
        {
            return Result.Failure<IReadOnlyList<IIncidentEvent>>(IncidentErrors.InvalidState(incident, "propose a plan once execution was requested"));
        }

        List<IIncidentEvent> events = [new PlanProposed(plan, plan.ProposedAt)];
        if (plan.Policy == PolicyVerdict.Deny)
        {
            events.Add(new IncidentEscalated($"Policy {plan.PolicyVersion} denies the plan; human remediation is required.", plan.ProposedAt));
        }
        else if (plan.Policy == PolicyVerdict.SuggestionOnly)
        {
            events.Add(new IncidentEscalated($"Suggested remediation, which policy {plan.PolicyVersion} leaves to responders: {plan.Summary ?? "see the plan"}.", plan.ProposedAt));
        }

        return Result.Success<IReadOnlyList<IIncidentEvent>>(events);
    }

    /// <summary>Relays the Gateway's signed approval. Only the current plan version, with its exact hash, can be approved.</summary>
    public Result<IReadOnlyList<IIncidentEvent>> Approve(PlanId planId, int planVersion, string planHash, string approverId, string attestation, DateTimeOffset now, IncidentState incident)
    {
        Result current = RequireCurrentPlan(planId, planVersion, planHash);
        if (current.IsFailure)
        {
            return Result.Failure<IReadOnlyList<IIncidentEvent>>(current.Error);
        }

        if (Approval is not null)
        {
            return Approval.PlanVersion == planVersion ? Unchanged() : Result.Failure<IReadOnlyList<IIncidentEvent>>(IncidentErrors.AlreadyApproved);
        }

        if (!IncidentStateMachine.Allows(incident, IncidentOperation.Approve) || !CanBecome(RemediationState.Approved))
        {
            return Result.Failure<IReadOnlyList<IIncidentEvent>>(IncidentErrors.InvalidState(incident, "approve"));
        }

        return Decided([new PlanApproved(planId, planVersion, planHash, approverId, attestation, now)]);
    }

    public Result<IReadOnlyList<IIncidentEvent>> Reject(PlanId planId, int planVersion, string rejectedBy, string reason, DateTimeOffset now, IncidentState incident)
    {
        if (!IsCurrentPlan(planId, planVersion))
        {
            return Result.Failure<IReadOnlyList<IIncidentEvent>>(IncidentErrors.NotCurrentPlan);
        }

        if (Rejection is { } existing && existing.PlanId == planId && existing.PlanVersion == planVersion)
        {
            return Unchanged();
        }

        if (!IncidentStateMachine.Allows(incident, IncidentOperation.Reject) || !CanBecome(RemediationState.Rejected))
        {
            return Result.Failure<IReadOnlyList<IIncidentEvent>>(IncidentErrors.InvalidState(incident, "reject"));
        }

        return Decided([new PlanRejected(planId, planVersion, rejectedBy, reason, now), new IncidentEscalated($"{rejectedBy} rejected the plan: {reason}", now)]);
    }

    /// <summary>Withdraws an approval before execution starts (Phase 06); afterwards no start can be confirmed.</summary>
    public Result<IReadOnlyList<IIncidentEvent>> Revoke(PlanId planId, int planVersion, string revokedBy, string reason, DateTimeOffset now, IncidentState incident)
    {
        if (!IsCurrentPlan(planId, planVersion))
        {
            return Result.Failure<IReadOnlyList<IIncidentEvent>>(IncidentErrors.NotCurrentPlan);
        }

        if (Approval is null)
        {
            return Unchanged();
        }

        if (ExecutionStart is not null || !CanBecome(RemediationState.Revoked))
        {
            return Result.Failure<IReadOnlyList<IIncidentEvent>>(IncidentErrors.InvalidState(incident, "revoke an approval after execution started"));
        }

        return Decided([new ApprovalRevoked(planId, planVersion, revokedBy, reason, now), new IncidentEscalated($"{revokedBy} revoked the approval of plan v{planVersion}: {reason}", now)]);
    }

    /// <summary>Requests execution of the approved plan; <paramref name="fencingToken"/> is the new event's stream version.</summary>
    public Result<ExecutionRequested> RequestExecution(string idempotencyKey, long fencingToken, DateTimeOffset now, IncidentState incident)
    {
        if (Execution is not null)
        {
            return Result.Success(Execution);
        }

        if (Approval is null || Plan is null || !IncidentStateMachine.Allows(incident, IncidentOperation.RequestExecution) || !CanBecome(RemediationState.ExecutionRequested))
        {
            return Result.Failure<ExecutionRequested>(IncidentErrors.InvalidState(incident, "request execution"));
        }

        return Result.Success(new ExecutionRequested(Plan.PlanId, Plan.Version, fencingToken, idempotencyKey, now));
    }

    /// <summary>
    /// Executor's start confirmation: only for the requested plan and the fencing token it was issued
    /// with, while the incident is still remediating. A repeated confirmation changes nothing.
    /// </summary>
    public Result<IReadOnlyList<IIncidentEvent>> ConfirmStart(PlanId planId, int planVersion, long fencingToken, DateTimeOffset now, IncidentState incident)
    {
        if (Execution is null || Execution.PlanId != planId || Execution.PlanVersion != planVersion)
        {
            return Result.Failure<IReadOnlyList<IIncidentEvent>>(IncidentErrors.NotCurrentPlan);
        }

        if (Execution.FencingToken != fencingToken)
        {
            return Result.Failure<IReadOnlyList<IIncidentEvent>>(IncidentErrors.StaleFencingToken(fencingToken, Execution.FencingToken));
        }

        if (ExecutionStart is not null)
        {
            return Unchanged();
        }

        if (!IncidentStateMachine.Allows(incident, IncidentOperation.ConfirmStart) || !CanBecome(RemediationState.Executing))
        {
            return Result.Failure<IReadOnlyList<IIncidentEvent>>(IncidentErrors.InvalidState(incident, "start remediation"));
        }

        return Decided([new ExecutionStarted(planId, planVersion, fencingToken, now)]);
    }

    /// <summary>Records Executor's result; an outcome Executor could not determine keeps the remediation open until reconciled.</summary>
    public Result<IReadOnlyList<IIncidentEvent>> RecordExecution(PlanId planId, int planVersion, ExecutionResult result, string? detail, DateTimeOffset now)
    {
        if (Execution is null || Execution.PlanId != planId || Execution.PlanVersion != planVersion)
        {
            return Result.Failure<IReadOnlyList<IIncidentEvent>>(IncidentErrors.NotCurrentPlan);
        }

        // A determined outcome is final; only an unknown one may be reported again.
        if (!CanBecome(RemediationState.Finished))
        {
            return Unchanged();
        }

        List<IIncidentEvent> events = [new ExecutionReported(planId, planVersion, result, detail, now)];
        if (result is ExecutionResult.Rejected or ExecutionResult.Aborted or ExecutionResult.Failed or ExecutionResult.Compensated or ExecutionResult.CompensationFailed)
        {
            events.Add(new IncidentEscalated($"Remediation ended {result}: {detail}", now));
        }

        return Result.Success<IReadOnlyList<IIncidentEvent>>(events);
    }

    public void Apply(IIncidentEvent @event)
    {
        switch (@event)
        {
            case PlanProposed proposed:
                Plan = proposed.Plan;
                Approval = null;
                break;
            case PlanApproved approved:
                Approval = approved;
                break;
            case PlanRejected rejected:
                Rejection = rejected;
                break;
            case ApprovalRevoked revoked:
                Approval = null;
                Revocation = revoked;
                break;
            case ExecutionRequested execution:
                Execution = execution;
                break;
            case ExecutionStarted started:
                ExecutionStart = started;
                break;
            case ExecutionReported reported:
                ExecutionOutcome = reported;
                break;
            case DecisionSuperseded:
                // The superseded decision's plan and approval stay in the history; a new decision starts over.
                Plan = null;
                Approval = null;
                Rejection = null;
                break;
            case IncidentReopened:
                Plan = null;
                Approval = null;
                Rejection = null;
                Revocation = null;
                Execution = null;
                ExecutionStart = null;
                ExecutionOutcome = null;
                break;
        }
    }

    private static Result<IReadOnlyList<IIncidentEvent>> Unchanged() => Result.Success<IReadOnlyList<IIncidentEvent>>([]);

    private bool CanBecome(RemediationState next) => Transitions[State].Contains(next);

    private static Result<IReadOnlyList<IIncidentEvent>> Decided(IReadOnlyList<IIncidentEvent> events) => Result.Success(events);

    private bool IsCurrentPlan(PlanId planId, int planVersion) => Plan is not null && Plan.PlanId == planId && Plan.Version == planVersion;

    private Result RequireCurrentPlan(PlanId planId, int planVersion, string planHash)
    {
        if (!IsCurrentPlan(planId, planVersion))
        {
            return Result.Failure(IncidentErrors.NotCurrentPlan);
        }

        return Plan!.Hash == planHash ? Result.Success() : Result.Failure(IncidentErrors.PlanHashMismatch);
    }
}
