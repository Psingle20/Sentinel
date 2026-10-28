using CleanArchitecture.BuildingBlocks;
using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.Executor.Domain.Executions;

/// <summary>Executor's durable execution states (ADR-006).</summary>
public enum ExecutionState
{
    Received,
    Rejected,
    Reserved,
    Aborted,
    StartConfirmed,
    Executing,
    ActionVerified,
    Failed,
    OutcomeUnknown,
    Reconciling,
    Compensating,
    Compensated,
    CompensationFailed
}

public sealed record ExecutionTransition(ExecutionState From, ExecutionState To, string? Detail, DateTimeOffset At);

/// <summary>
/// One execute request and its state machine. Every transition is persisted before the next
/// external call, so after a crash the request's state says exactly how far it got.
/// <see cref="ExecutionState.OutcomeUnknown"/> is reconciled by inspecting the target, never by
/// repeating the action.
/// </summary>
public sealed class ExecutionRequest
{
    private static readonly Dictionary<ExecutionState, ExecutionState[]> Allowed = new()
    {
        [ExecutionState.Received] = [ExecutionState.Rejected, ExecutionState.Reserved],
        [ExecutionState.Reserved] = [ExecutionState.Aborted, ExecutionState.StartConfirmed],
        [ExecutionState.StartConfirmed] = [ExecutionState.Executing, ExecutionState.Aborted],
        [ExecutionState.Executing] = [ExecutionState.ActionVerified, ExecutionState.Failed, ExecutionState.OutcomeUnknown],
        [ExecutionState.OutcomeUnknown] = [ExecutionState.Reconciling],
        [ExecutionState.Reconciling] = [ExecutionState.ActionVerified, ExecutionState.Failed, ExecutionState.Reconciling],
        [ExecutionState.Failed] = [ExecutionState.Compensating],
        [ExecutionState.Compensating] = [ExecutionState.Compensated, ExecutionState.CompensationFailed]
    };

    private readonly List<ExecutionTransition> _history = [];
    private readonly List<ActionId> _completedActions = [];

    private ExecutionRequest()
    {
    }

    public required Guid RequestId { get; init; }

    public required string IdempotencyKey { get; init; }

    public required TenantId TenantId { get; init; }

    public required EnvironmentId EnvironmentId { get; init; }

    public required IncidentId IncidentId { get; init; }

    public required PlanId PlanId { get; init; }

    public required int PlanVersion { get; init; }

    public required string PlanHash { get; init; }

    public required long FencingToken { get; init; }

    /// <summary>The plan document as received, in canonical contract JSON.</summary>
    public required string Plan { get; init; }

    public AttestationId? AttestationId { get; private set; }

    public string? ApproverId { get; private set; }

    public string? PolicyVersion { get; private set; }

    public ExecutionState State { get; private set; }

    public string? Detail { get; private set; }

    public DateTimeOffset CreatedAt { get; private init; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public IReadOnlyList<ExecutionTransition> History => _history;

    /// <summary>Actions that completed and were verified, in order: what compensation must undo.</summary>
    public IReadOnlyList<ActionId> CompletedActions => _completedActions;

    /// <summary>True when the result reported to Runtime is final for this request.</summary>
    public bool HasFinalOutcome => State is ExecutionState.Rejected or ExecutionState.Aborted or ExecutionState.ActionVerified
        or ExecutionState.Failed or ExecutionState.Compensated or ExecutionState.CompensationFailed;

    public static ExecutionRequest Receive(
        string idempotencyKey,
        TenantId tenantId,
        EnvironmentId environmentId,
        IncidentId incidentId,
        PlanId planId,
        int planVersion,
        string planHash,
        long fencingToken,
        string plan,
        DateTimeOffset now) => new()
        {
            RequestId = Guid.CreateVersion7(now),
            IdempotencyKey = idempotencyKey,
            TenantId = tenantId,
            EnvironmentId = environmentId,
            IncidentId = incidentId,
            PlanId = planId,
            PlanVersion = planVersion,
            PlanHash = planHash,
            FencingToken = fencingToken,
            Plan = plan,
            State = ExecutionState.Received,
            CreatedAt = now,
            UpdatedAt = now
        };

    /// <summary>Restores a persisted request.</summary>
    public static ExecutionRequest Restore(ExecutionSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var request = new ExecutionRequest
        {
            RequestId = snapshot.RequestId,
            IdempotencyKey = snapshot.IdempotencyKey,
            TenantId = snapshot.TenantId,
            EnvironmentId = snapshot.EnvironmentId,
            IncidentId = snapshot.IncidentId,
            PlanId = snapshot.PlanId,
            PlanVersion = snapshot.PlanVersion,
            PlanHash = snapshot.PlanHash,
            FencingToken = snapshot.FencingToken,
            Plan = snapshot.Plan,
            AttestationId = snapshot.AttestationId,
            ApproverId = snapshot.ApproverId,
            PolicyVersion = snapshot.PolicyVersion,
            State = snapshot.State,
            Detail = snapshot.Detail,
            CreatedAt = snapshot.CreatedAt,
            UpdatedAt = snapshot.UpdatedAt
        };
        request._history.AddRange(snapshot.History);
        request._completedActions.AddRange(snapshot.CompletedActions ?? []);
        return request;
    }

    public ExecutionSnapshot Snapshot() => new(
        RequestId, IdempotencyKey, TenantId, EnvironmentId, IncidentId, PlanId, PlanVersion, PlanHash, FencingToken, Plan,
        AttestationId, ApproverId, PolicyVersion, State, Detail, CreatedAt, UpdatedAt, [.. _history], [.. _completedActions]);

    /// <summary>Records that one action of the plan completed and was verified, before the next one starts.</summary>
    public Result ActionCompleted(ActionId actionId, DateTimeOffset now)
    {
        if (State != ExecutionState.Executing)
        {
            return Result.Failure(Error.Conflict("Execution.InvalidTransition", $"Actions complete only while executing, not in {State}."));
        }

        if (!_completedActions.Contains(actionId))
        {
            _completedActions.Add(actionId);
            UpdatedAt = now;
        }

        return Result.Success();
    }

    public Result Reject(string reason, DateTimeOffset now) => Move(ExecutionState.Rejected, reason, now);

    /// <summary>Claims the attestation and the idempotency key; nothing has touched the target yet.</summary>
    public Result Reserve(AttestationId attestationId, string approverId, string policyVersion, DateTimeOffset now)
    {
        Result moved = Move(ExecutionState.Reserved, $"Attestation {attestationId} by {approverId} under policy {policyVersion}", now);
        if (moved.IsSuccess)
        {
            AttestationId = attestationId;
            ApproverId = approverId;
            PolicyVersion = policyVersion;
        }

        return moved;
    }

    public Result Abort(string reason, DateTimeOffset now) => Move(ExecutionState.Aborted, reason, now);

    public Result ConfirmStart(DateTimeOffset now) => Move(ExecutionState.StartConfirmed, "Runtime confirmed the start.", now);

    public Result BeginExecuting(DateTimeOffset now) => Move(ExecutionState.Executing, "The adapter is executing the action.", now);

    public Result Verified(string detail, DateTimeOffset now) => Move(ExecutionState.ActionVerified, detail, now);

    public Result Fail(string reason, DateTimeOffset now) => Move(ExecutionState.Failed, reason, now);

    public Result Unknown(string reason, DateTimeOffset now) => Move(ExecutionState.OutcomeUnknown, reason, now);

    public Result Reconcile(string detail, DateTimeOffset now) => Move(ExecutionState.Reconciling, detail, now);

    public Result BeginCompensating(DateTimeOffset now) => Move(ExecutionState.Compensating, "Compensating a partially applied action.", now);

    public Result Compensated(string detail, DateTimeOffset now) => Move(ExecutionState.Compensated, detail, now);

    public Result CompensationFailed(string detail, DateTimeOffset now) => Move(ExecutionState.CompensationFailed, detail, now);

    private Result Move(ExecutionState to, string? detail, DateTimeOffset now)
    {
        if (!Allowed.TryGetValue(State, out ExecutionState[]? next) || !next.Contains(to))
        {
            return Result.Failure(Error.Conflict("Execution.InvalidTransition", $"Cannot move from {State} to {to}."));
        }

        _history.Add(new ExecutionTransition(State, to, detail, now));
        State = to;
        Detail = detail;
        UpdatedAt = now;
        return Result.Success();
    }
}

/// <summary>The persisted form of an <see cref="ExecutionRequest"/>.</summary>
public sealed record ExecutionSnapshot(
    Guid RequestId,
    string IdempotencyKey,
    TenantId TenantId,
    EnvironmentId EnvironmentId,
    IncidentId IncidentId,
    PlanId PlanId,
    int PlanVersion,
    string PlanHash,
    long FencingToken,
    string Plan,
    AttestationId? AttestationId,
    string? ApproverId,
    string? PolicyVersion,
    ExecutionState State,
    string? Detail,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<ExecutionTransition> History,
    IReadOnlyList<ActionId>? CompletedActions = null);
