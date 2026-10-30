using System.Globalization;
using System.Text.Json;
using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Integration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sentinel.Contracts.Messaging;
using Sentinel.Contracts.Remediation;
using Sentinel.Executor.Domain.Capabilities;
using Sentinel.Executor.Domain.Executions;
using Sentinel.Executor.Domain.Policy;
using DomainVerdict = Sentinel.Executor.Domain.Policy.PolicyVerdict;
using Sentinel.Executor.Application.Abstractions.Persistence;
using Sentinel.Executor.Application.Abstractions.Providers;
using Sentinel.Executor.Application.Abstractions.Remediation;
using Sentinel.Executor.Application.Options;

namespace Sentinel.Executor.Application.Services;

/// <summary>
/// The durable execution state machine (ADR-006) behind <c>ExecuteRemediationCommandHandler</c>,
/// which verifies and reserves a request: confirm the start with Runtime, then for each action issue
/// a single-use capability, execute through the adapter and verify the action, and report. Each
/// transition is committed before the next external call; a request that stops part-way is finished
/// by <see cref="ReconcileAsync"/>, which inspects the target instead of repeating the action. A plan
/// that fails part-way is compensated: its completed actions are undone in reverse, or the failure is
/// escalated. It becomes processes and tasks in Executor's own refactoring step.
/// </summary>
public sealed class ExecutionService(
    IExecutionStore store,
    AttestationVerifier verifier,
    PolicyService policy,
    IStartConfirmation startConfirmation,
    IEnumerable<IRemediationAdapter> adapters,
    IOutbox outbox,
    IUnitOfWork unitOfWork,
    TimeProvider time,
    IOptions<ExecutorOptions> options,
    ILogger<ExecutionService> logger)
{
    /// <summary>Finishes a request that stopped part-way, from the state it reached.</summary>
    public async Task ReconcileAsync(ExecutionRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        RemediationPlan plan = JsonSerializer.Deserialize<RemediationPlan>(request.Plan, SentinelJson.Options)!;
        DateTimeOffset now = time.GetUtcNow();
        switch (request.State)
        {
            case ExecutionState.Received:
                request.Reject("Executor restarted before the request was reserved; nothing was executed.", now);
                await FinishAsync(request, cancellationToken);
                break;
            case ExecutionState.Reserved:
                request.Abort("Executor restarted before Runtime confirmed the start; nothing was executed.", now);
                await FinishAsync(request, cancellationToken);
                break;
            case ExecutionState.StartConfirmed:
                // The adapter is called only after Executing is committed, so it has not run yet.
                await ExecuteAsync(request, plan, cancellationToken);
                break;
            case ExecutionState.Executing:
                request.Unknown("Executor restarted while the action was executing.", now);
                await SaveAsync(request, cancellationToken);
                Publish(request);
                await unitOfWork.CommitAsync(cancellationToken);
                await ReconcileFromTargetAsync(request, plan, cancellationToken);
                break;
            case ExecutionState.OutcomeUnknown:
            case ExecutionState.Reconciling:
                await ReconcileFromTargetAsync(request, plan, cancellationToken);
                break;
            case ExecutionState.Compensating:
                // Compensation never finished: undo the recorded completed actions again; adapters' undo is idempotent.
                await CompensateAsync(request, plan, [.. plan.Actions.Where(action => request.CompletedActions.Contains(action.ActionId))], cancellationToken);
                break;
        }
    }

    /// <summary>
    /// Whether the command is genuine: the plan content matches its hash and the attestation was signed by
    /// the Gateway for exactly this plan. A command that fails here is not Runtime's request at all, so it
    /// binds nothing and is reported to nobody; policy and preconditions are checked only after this.
    /// </summary>
    internal async Task<Result<ApprovalAttestationClaims>> AuthenticateAsync(ExecuteRemediation command, CancellationToken cancellationToken)
    {
        if (!RemediationPlanHash.Matches(command.Plan))
        {
            return Result.Failure<ApprovalAttestationClaims>(Error.Problem("Attestation.Invalid", "The plan hash does not match the plan content."));
        }

        return await verifier.VerifyAsync(command.Attestation, command, cancellationToken);
    }

    /// <summary>Execution-time policy, an adapter for every action and the preconditions at the target, for a command already authenticated.</summary>
    internal async Task<Result> CheckAsync(ExecuteRemediation command, CancellationToken cancellationToken)
    {
        // Execution-time policy is authoritative; if it changed since plan time, it wins.
        PolicyEvaluation evaluation = await policy.EvaluateAsync(new PolicyEvaluationRequest(command.TenantId, command.EnvironmentId, command.IncidentId, command.Plan), cancellationToken);
        if (evaluation.Decision is PolicyDecision.Deny or PolicyDecision.SuggestionOnly)
        {
            return Refuse($"Policy {evaluation.PolicyVersion} does not permit execution ({evaluation.Decision}): {string.Join("; ", evaluation.RuleTrace)}");
        }

        foreach (RemediationAction action in command.Plan.Actions)
        {
            if (Adapter(action) is not { } adapter)
            {
                return Refuse($"No adapter executes {PolicyService.ActionKind(action)} actions.");
            }

            // Fail closed: a target that cannot be inspected cannot be shown to meet the preconditions, and nothing has
            // run yet. Letting the exception escape would leave the request received but never reserved, for
            // reconciliation to refuse later under a misleading reason.
            TargetInspection target;
            try
            {
                target = await adapter.InspectAsync(action, command.IdempotencyKey, cancellationToken);
            }
            catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
            {
                return Refuse($"{action.TargetServiceId} could not be inspected before executing: {exception.Message}");
            }

            if (action is RollbackAction rollback && target.ServingRevision != rollback.FromRevision)
            {
                return Refuse($"Precondition failed: {rollback.TargetServiceId} serves {target.ServingRevision ?? "nothing"}, not {rollback.FromRevision}.");
            }

            foreach (PlanPrecondition precondition in (command.Plan.Preconditions ?? []).Where(precondition => precondition.ServiceId == action.TargetServiceId))
            {
                if (!Holds(precondition, target))
                {
                    return Refuse($"Precondition failed: {precondition.ServiceId} {precondition.Condition} should be {precondition.Expected}; {target.Detail}");
                }
            }
        }

        return Result.Success();

        static Result Refuse(string reason) => Result.Failure(Error.Problem("Execution.Refused", reason));
    }

    /// <summary>Preconditions Executor can check from an inspection: what is serving, whether a batch is active, and whether a queue still redelivers a poison message.</summary>
    private static bool Holds(PlanPrecondition precondition, TargetInspection target) => precondition.Condition switch
    {
        "serving-revision" => target.ServingRevision == precondition.Expected,
        "batch-active" or "queue-state" => string.Equals(target.ServingRevision, precondition.Expected, StringComparison.OrdinalIgnoreCase),
        _ => false
    };

    internal async Task ConfirmAndExecuteAsync(ExecutionRequest request, RemediationPlan plan, CancellationToken cancellationToken)
    {
        RemediationStartResponse response;
        try
        {
            response = await startConfirmation.ConfirmAsync(
                new RemediationStartRequest(request.TenantId, request.EnvironmentId, request.IncidentId, request.PlanId, request.PlanVersion, request.FencingToken),
                cancellationToken);
        }
        catch (HttpRequestException exception)
        {
            // Fail closed: without Runtime's confirmation, nothing runs (ADR-006).
            response = new RemediationStartResponse(false, $"Runtime could not be reached: {exception.Message}");
        }

        if (!response.Confirmed)
        {
            request.Abort($"Runtime refused the start: {response.Reason}", time.GetUtcNow());
            await FinishAsync(request, cancellationToken);
            return;
        }

        request.ConfirmStart(time.GetUtcNow());
        await SaveAsync(request, cancellationToken);
        await ExecuteAsync(request, plan, cancellationToken);
    }

    private async Task ExecuteAsync(ExecutionRequest request, RemediationPlan plan, CancellationToken cancellationToken)
    {
        request.BeginExecuting(time.GetUtcNow());
        await SaveAsync(request, cancellationToken);

        var verified = new List<string>();
        var completed = new List<RemediationAction>();
        foreach (RemediationAction action in plan.Actions)
        {
            IRemediationAdapter adapter = Adapter(action)!;
            Capability? capability = await IssueAsync(request, action, adapter, cancellationToken);
            if (capability is null)
            {
                await FailAsync(request, plan, completed, $"The capability for {PolicyService.ActionKind(action)} on {action.TargetServiceId} could not be claimed.", cancellationToken);
                return;
            }

            AdapterResult result;
            try
            {
                result = await adapter.ExecuteAsync(action, capability, request.IdempotencyKey, cancellationToken);
            }
            catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
            {
                result = new AdapterResult(AdapterOutcome.Unknown, $"The adapter call ended without a clear answer: {exception.Message}");
            }

            if (result.Outcome == AdapterOutcome.Rejected)
            {
                await FailAsync(request, plan, completed, $"The target refused the action: {result.Detail}", cancellationToken);
                return;
            }

            if (result.Outcome == AdapterOutcome.Unknown || !await VerifyAsync(adapter, action, request.IdempotencyKey, cancellationToken))
            {
                request.Unknown(result.Outcome == AdapterOutcome.Unknown ? result.Detail : "The action was accepted but not verified in time.", time.GetUtcNow());
                await SaveAsync(request, cancellationToken);
                Publish(request);
                await unitOfWork.CommitAsync(cancellationToken);
                return;
            }

            completed.Add(action);
            verified.Add(result.Detail);
            request.ActionCompleted(action.ActionId, time.GetUtcNow());
            await SaveAsync(request, cancellationToken);
        }

        request.Verified(string.Join(" ", verified), time.GetUtcNow());
        await FinishAsync(request, cancellationToken);
    }

    /// <summary>Issues and claims a single-use capability scoped to one action, recorded before the adapter is called.</summary>
    private async Task<Capability?> IssueAsync(ExecutionRequest request, RemediationAction action, IRemediationAdapter adapter, CancellationToken cancellationToken)
    {
        var capability = Capability.Issue(
            request.RequestId,
            PolicyService.ActionKind(action),
            action.TargetServiceId.Value,
            request.TenantId,
            request.EnvironmentId,
            adapter.CapabilityLimits(action),
            time.GetUtcNow(),
            options.Value.CapabilityLifetime);
        await store.SaveCapabilityAsync(capability, cancellationToken);
        bool claimed = await store.TryConsumeCapabilityAsync(capability.CapabilityId, cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
        return claimed ? capability : null;
    }

    /// <summary>A failure part-way through a plan: fail, then compensate any action that already completed.</summary>
    private async Task FailAsync(ExecutionRequest request, RemediationPlan plan, List<RemediationAction> completed, string reason, CancellationToken cancellationToken)
    {
        request.Fail(reason, time.GetUtcNow());
        if (completed.Count == 0)
        {
            await FinishAsync(request, cancellationToken);
            return;
        }

        // Failed is final, so it is never persisted alone: compensation starts in the same commit.
        request.BeginCompensating(time.GetUtcNow());
        await SaveAsync(request, cancellationToken);
        await CompensateAsync(request, plan, completed, cancellationToken);
    }

    /// <summary>The saga's compensation: undo completed actions in reverse order; any that cannot be undone escalates.</summary>
    private async Task CompensateAsync(ExecutionRequest request, RemediationPlan plan, IReadOnlyList<RemediationAction> completed, CancellationToken cancellationToken)
    {
        if (request.State == ExecutionState.Failed)
        {
            request.BeginCompensating(time.GetUtcNow());
            await SaveAsync(request, cancellationToken);
        }

        var undone = new List<string>();
        foreach (RemediationAction action in completed.Reverse())
        {
            IRemediationAdapter adapter = Adapter(action)!;
            Capability? capability = await IssueAsync(request, action, adapter, cancellationToken);
            AdapterResult result = capability is null
                ? new AdapterResult(AdapterOutcome.Rejected, "The compensation capability could not be claimed.")
                : await adapter.CompensateAsync(action, capability, request.IdempotencyKey, cancellationToken);
            if (result.Outcome != AdapterOutcome.Accepted)
            {
                request.CompensationFailed($"Compensating {PolicyService.ActionKind(action)} on {action.TargetServiceId} failed: {result.Detail} Responders must restore it.", time.GetUtcNow());
                await FinishAsync(request, cancellationToken);
                return;
            }

            undone.Add(result.Detail);
        }

        request.Compensated($"Compensated {undone.Count} of {plan.Actions.Count} action(s): {string.Join(" ", undone)}", time.GetUtcNow());
        await FinishAsync(request, cancellationToken);
    }

    private async Task<bool> VerifyAsync(IRemediationAdapter adapter, RemediationAction action, string idempotencyKey, CancellationToken cancellationToken)
    {
        ExecutorOptions settings = options.Value;
        DateTimeOffset deadline = time.GetUtcNow() + settings.VerifyTimeout;
        while (time.GetUtcNow() < deadline)
        {
            if (await adapter.VerifyActionAsync(action, idempotencyKey, cancellationToken))
            {
                return true;
            }

            await Task.Delay(settings.VerifyPollInterval, time, cancellationToken);
        }

        return false;
    }

    private async Task ReconcileFromTargetAsync(ExecutionRequest request, RemediationPlan plan, CancellationToken cancellationToken)
    {
        var states = new List<TargetInspection>();
        foreach (RemediationAction action in plan.Actions)
        {
            states.Add(await Adapter(action)!.InspectAsync(action, request.IdempotencyKey, cancellationToken));
        }

        DateTimeOffset now = time.GetUtcNow();
        if (request.State == ExecutionState.OutcomeUnknown)
        {
            request.Reconcile("Inspecting the target to learn what happened.", now);
        }

        bool allApplied = plan.Actions.Select((action, index) => (action, state: states[index]))
            .All(pair => pair.state.Operation == OperationStatus.Completed && (pair.action is not RollbackAction rollback || pair.state.ServingRevision == rollback.ToRevision));

        if (allApplied)
        {
            request.Verified("Reconciled: the target shows the action completed. " + string.Join(" ", states.Select(state => state.Detail)), now);
            await FinishAsync(request, cancellationToken);
        }
        else if (states.Any(state => state.Operation is OperationStatus.Failed or OperationStatus.NotFound))
        {
            request.Fail("Reconciled: the action did not take effect and was not repeated. " + string.Join(" ", states.Select(state => state.Detail)), now);
            RemediationAction[] applied = [.. plan.Actions.Where((action, index) => states[index].Operation == OperationStatus.Completed)];
            if (applied.Length == 0)
            {
                await FinishAsync(request, cancellationToken);
            }
            else
            {
                request.BeginCompensating(now);
                await SaveAsync(request, cancellationToken);
                await CompensateAsync(request, plan, applied, cancellationToken);
            }
        }
        else
        {
            request.Reconcile("The action is still in progress at the target.", now);
            await SaveAsync(request, cancellationToken);
            await unitOfWork.CommitAsync(cancellationToken);
        }
    }

    private IRemediationAdapter? Adapter(RemediationAction action) =>
        adapters.FirstOrDefault(adapter => adapter.ActionKind == PolicyService.ActionKind(action));

    internal async Task SaveAsync(ExecutionRequest request, CancellationToken cancellationToken)
    {
        await store.SaveAsync(request, cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
    }

    internal async Task FinishAsync(ExecutionRequest request, CancellationToken cancellationToken)
    {
        await store.SaveAsync(request, cancellationToken);
        Publish(request);
        await unitOfWork.CommitAsync(cancellationToken);
        logger.LogInformation("Execution {Key} for incident {IncidentId} ended {State}: {Detail}", request.IdempotencyKey, request.IncidentId, request.State, request.Detail);
    }

    internal void Publish(ExecutionRequest request) =>
        outbox.Enqueue(
            new RemediationExecuted
            {
                TenantId = request.TenantId,
                EnvironmentId = request.EnvironmentId,
                IncidentId = request.IncidentId,
                PlanId = request.PlanId,
                PlanVersion = request.PlanVersion,
                Outcome = request.State switch
                {
                    ExecutionState.Rejected => ExecutionOutcome.Rejected,
                    ExecutionState.Aborted => ExecutionOutcome.Aborted,
                    ExecutionState.ActionVerified => ExecutionOutcome.ActionVerified,
                    ExecutionState.Failed => ExecutionOutcome.Failed,
                    ExecutionState.Compensated => ExecutionOutcome.Compensated,
                    ExecutionState.CompensationFailed => ExecutionOutcome.CompensationFailed,
                    _ => ExecutionOutcome.OutcomeUnknown
                },
                Detail = request.Detail,
                CompletedAt = request.UpdatedAt
            },
            new MessageMetadata(
                CorrelationId: request.IncidentId.ToString(),
                Headers: SentinelHeaders.For(request.TenantId, request.EnvironmentId, incidentId: request.IncidentId)));

    public static string Describe(ExecutionRequest request) =>
        string.Create(CultureInfo.InvariantCulture, $"{request.IdempotencyKey} {request.State} at {request.UpdatedAt:O}");
}
