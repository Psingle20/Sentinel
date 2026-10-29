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

namespace Sentinel.Executor.Application;

/// <summary>Configured as <c>Executor</c>.</summary>
public sealed class ExecutorOptions
{
    public const string Section = "Executor";

    /// <summary>Denies every action, independent of Runtime, Api and every model path (ADR-006). Operators can also engage it at run time.</summary>
    public bool EmergencyStop { get; set; }

    /// <summary>The remediation policy document, relative to the content root.</summary>
    public string PolicyFile { get; set; } = "policies/remediation-policy.json";

    /// <summary>How long action verification waits for the target to show the action took effect.</summary>
    public TimeSpan VerifyTimeout { get; set; } = TimeSpan.FromSeconds(90);

    public TimeSpan VerifyPollInterval { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>An unfinished request untouched for this long is reconciled.</summary>
    public TimeSpan ReconcileAfter { get; set; } = TimeSpan.FromSeconds(20);

    /// <summary>A capability expires this long after it is issued; the adapter call must start before then.</summary>
    public TimeSpan CapabilityLifetime { get; set; } = TimeSpan.FromMinutes(2);
}

/// <summary>
/// The only policy engine (ADR-006): advisory at plan time for Runtime, authoritative at execution
/// time. It evaluates the versioned policy document with the operator controls; a plan's verdict is
/// the most restrictive verdict of its actions, and every evaluation records its rule trace.
/// </summary>
public sealed class PolicyService(IPolicyStore policies, IControlStore controls, IOptions<ExecutorOptions> options, TimeProvider time)
{
    public async Task<PolicyEvaluation> EvaluateAsync(PolicyEvaluationRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        PolicyDocument policy = await policies.CurrentAsync(cancellationToken);
        DateTimeOffset now = time.GetUtcNow();
        if (!RemediationPlanHash.Matches(request.Plan))
        {
            return new PolicyEvaluation(PolicyDecision.Deny, policy.Version, ["plan.hash: the hash does not match the plan content"], now);
        }

        ExecutorControls current = await controls.GetAsync(cancellationToken);
        if (options.Value.EmergencyStop)
        {
            current = current with { EmergencyStop = true };
        }

        int blastRadius = request.Plan.BlastRadius?.Count ?? request.Plan.Actions.Select(action => action.TargetServiceId).Distinct().Count();
        PolicyOutcome[] outcomes = [.. request.Plan.Actions.Select(action => PolicyEngine.Evaluate(
            policy,
            new PolicyInput(
                request.TenantId.Value,
                request.EnvironmentId.Value,
                ActionKind(action),
                action.TargetServiceId.Value,
                request.Plan.Risk.ToString().ToLowerInvariant(),
                request.Confidence,
                request.Severity?.ToString().ToLowerInvariant(),
                blastRadius,
                now),
            current))];

        DomainVerdict verdict = outcomes.Length == 0
            ? DomainVerdict.Deny
            : outcomes.Select(outcome => outcome.Verdict).Aggregate(PolicyEngine.MostRestrictive);
        return new PolicyEvaluation(
            verdict switch
            {
                DomainVerdict.Allow => PolicyDecision.Allow,
                DomainVerdict.ApprovalRequired => PolicyDecision.ApprovalRequired,
                DomainVerdict.SuggestionOnly => PolicyDecision.SuggestionOnly,
                _ => PolicyDecision.Deny
            },
            policy.Version,
            outcomes.Length == 0 ? ["plan: no actions"] : [.. outcomes.SelectMany(outcome => outcome.RuleTrace)],
            now);
    }

    public static string ActionKind(RemediationAction action) => action switch
    {
        RollbackAction => "rollback",
        PauseBatchAction => "pause-batch",
        _ => action.GetType().Name
    };
}

/// <summary>
/// Executes approved plans through the durable state machine (ADR-006): verify, reserve, confirm the
/// start with Runtime, then for each action issue a single-use capability, execute through the
/// adapter and verify the action, and report. Each transition is committed before the next
/// external call; a request that stops part-way is finished by <see cref="ReconcileAsync"/>, which
/// inspects the target instead of repeating the action. A plan that fails part-way is compensated:
/// its completed actions are undone in reverse, or the failure is escalated.
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
    public async Task HandleAsync(ExecuteRemediation command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (await store.FindAsync(command.IdempotencyKey, cancellationToken) is { } existing)
        {
            // A repeated request never runs the action again. A finished one reports its result again;
            // an unfinished one is left to reconciliation.
            if (existing.HasFinalOutcome)
            {
                Publish(existing);
            }

            await unitOfWork.CommitAsync(cancellationToken);
            return;
        }

        RemediationPlan plan = command.Plan;
        var request = ExecutionRequest.Receive(
            command.IdempotencyKey, command.TenantId, command.EnvironmentId, command.IncidentId,
            plan.PlanId, plan.PlanVersion, plan.PlanHash, command.FencingToken,
            JsonSerializer.Serialize(plan, SentinelJson.Options), time.GetUtcNow());
        await SaveAsync(request, cancellationToken);

        Result<ApprovalAttestationClaims> approval = await ValidateAsync(command, cancellationToken);
        if (approval.IsFailure)
        {
            request.Reject(approval.Error.Description, time.GetUtcNow());
            await FinishAsync(request, cancellationToken);
            return;
        }

        if (!await store.TryRaiseFenceAsync(command.IncidentId, command.FencingToken, cancellationToken))
        {
            request.Reject($"Fencing token {command.FencingToken} is lower than one already seen for the incident.", time.GetUtcNow());
            await FinishAsync(request, cancellationToken);
            return;
        }

        if (!await store.TryConsumeAttestationAsync(approval.Value.AttestationId, request.RequestId, cancellationToken))
        {
            request.Reject($"Attestation {approval.Value.AttestationId} was already used.", time.GetUtcNow());
            await FinishAsync(request, cancellationToken);
            return;
        }

        request.Reserve(approval.Value.AttestationId, approval.Value.ApproverId, approval.Value.PolicyVersion, time.GetUtcNow());
        await SaveAsync(request, cancellationToken);

        await ConfirmAndExecuteAsync(request, plan, cancellationToken);
    }

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

    private async Task<Result<ApprovalAttestationClaims>> ValidateAsync(ExecuteRemediation command, CancellationToken cancellationToken)
    {
        if (!RemediationPlanHash.Matches(command.Plan))
        {
            return Refuse("The plan hash does not match the plan content.");
        }

        Result<ApprovalAttestationClaims> attestation = await verifier.VerifyAsync(command.Attestation, command, cancellationToken);
        if (attestation.IsFailure)
        {
            return attestation;
        }

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

            TargetInspection target = await adapter.InspectAsync(action, command.IdempotencyKey, cancellationToken);
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

        return attestation;

        static Result<ApprovalAttestationClaims> Refuse(string reason) =>
            Result.Failure<ApprovalAttestationClaims>(Error.Problem("Execution.Refused", reason));
    }

    /// <summary>Preconditions Executor can check from an inspection: what is serving, and whether a batch is active.</summary>
    private static bool Holds(PlanPrecondition precondition, TargetInspection target) => precondition.Condition switch
    {
        "serving-revision" => target.ServingRevision == precondition.Expected,
        "batch-active" => string.Equals(target.ServingRevision, precondition.Expected, StringComparison.OrdinalIgnoreCase),
        _ => false
    };

    private async Task ConfirmAndExecuteAsync(ExecutionRequest request, RemediationPlan plan, CancellationToken cancellationToken)
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

    private async Task SaveAsync(ExecutionRequest request, CancellationToken cancellationToken)
    {
        await store.SaveAsync(request, cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
    }

    private async Task FinishAsync(ExecutionRequest request, CancellationToken cancellationToken)
    {
        await store.SaveAsync(request, cancellationToken);
        Publish(request);
        await unitOfWork.CommitAsync(cancellationToken);
        logger.LogInformation("Execution {Key} for incident {IncidentId} ended {State}: {Detail}", request.IdempotencyKey, request.IncidentId, request.State, request.Detail);
    }

    private void Publish(ExecutionRequest request) =>
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
