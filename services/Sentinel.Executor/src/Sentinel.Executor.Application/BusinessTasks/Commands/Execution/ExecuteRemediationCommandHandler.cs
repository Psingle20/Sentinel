using System.Text.Json;
using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Integration;
using CleanArchitecture.BuildingBlocks.Messaging;
using Microsoft.Extensions.Logging;
using Sentinel.Contracts.Messaging;
using Sentinel.Contracts.Remediation;
using Sentinel.Executor.Domain.Executions;
using Sentinel.Executor.Application.Abstractions.Persistence;
using Sentinel.Executor.Application.Services;

namespace Sentinel.Executor.Application.BusinessTasks.Commands.Execution;

/// <summary>
/// Takes an execution request (ADR-006). A repeated one never runs the action again. A new one is
/// authenticated first: the plan must match its hash and the attestation must be the Gateway's for
/// exactly this plan. Only then is it received under its idempotency key, so a command that was not
/// Runtime's cannot occupy the key the genuine command will arrive under, and nothing is reported to
/// Runtime for it. Policy and preconditions are checked next, the fencing token must be the highest
/// seen for the incident and the attestation unused, and only then is the request reserved and handed
/// to <see cref="ExecutionService"/> to confirm the start with Runtime and execute. Every refusal of
/// an authenticated command is final and reported; nothing here retries.
/// </summary>
public sealed class ExecuteRemediationCommandHandler(
    IExecutionStore store,
    ExecutionService executions,
    IUnitOfWork unitOfWork,
    TimeProvider time,
    ILogger<ExecuteRemediationCommandHandler> logger) : ICommandHandler<ExecuteRemediationCommand>
{
    /// <summary>Separates the record of an unauthenticated command from the key a genuine one binds.</summary>
    public const string UnverifiedKeySuffix = "#unverified:";

    public async Task<Result> Handle(ExecuteRemediationCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        ExecuteRemediation message = command.Message;
        if (await store.FindAsync(message.IdempotencyKey, cancellationToken) is { } existing)
        {
            // A repeated request never runs the action again. A finished one reports its result again;
            // an unfinished one is left to reconciliation.
            if (existing.HasFinalOutcome)
            {
                executions.Publish(existing);
            }

            await unitOfWork.CommitAsync(cancellationToken);
            return Result.Success();
        }

        RemediationPlan plan = message.Plan;
        DateTimeOffset now = time.GetUtcNow();
        Result<ApprovalAttestationClaims> approval = await executions.AuthenticateAsync(message, cancellationToken);
        if (approval.IsFailure)
        {
            // Not Runtime's command: kept for audit under its own key, reported to nobody.
            ExecutionRequest unverified = Receive(message, $"{message.IdempotencyKey}{UnverifiedKeySuffix}{Guid.CreateVersion7(now):N}", now);
            unverified.Reject(approval.Error.Description, now);
            await store.SaveAsync(unverified, cancellationToken);
            await unitOfWork.CommitAsync(cancellationToken);
            logger.LogWarning("Refused an unauthenticated execute command for incident {IncidentId} plan {PlanId} v{Version}: {Reason}", message.IncidentId, plan.PlanId, plan.PlanVersion, approval.Error.Description);
            return Result.Success();
        }

        ExecutionRequest request = Receive(message, message.IdempotencyKey, now);
        await executions.SaveAsync(request, cancellationToken);

        Result admitted = await executions.CheckAsync(message, cancellationToken);
        if (admitted.IsFailure)
        {
            request.Reject(admitted.Error.Description, time.GetUtcNow());
            await executions.FinishAsync(request, cancellationToken);
            return Result.Success();
        }

        if (!await store.TryRaiseFenceAsync(message.IncidentId, message.FencingToken, cancellationToken))
        {
            request.Reject($"Fencing token {message.FencingToken} is lower than one already seen for the incident.", time.GetUtcNow());
            await executions.FinishAsync(request, cancellationToken);
            return Result.Success();
        }

        if (!await store.TryConsumeAttestationAsync(approval.Value.AttestationId, request.RequestId, cancellationToken))
        {
            request.Reject($"Attestation {approval.Value.AttestationId} was already used.", time.GetUtcNow());
            await executions.FinishAsync(request, cancellationToken);
            return Result.Success();
        }

        request.Reserve(approval.Value.AttestationId, approval.Value.ApproverId, approval.Value.PolicyVersion, time.GetUtcNow());
        await executions.SaveAsync(request, cancellationToken);

        await executions.ConfirmAndExecuteAsync(request, plan, cancellationToken);
        return Result.Success();
    }

    private static ExecutionRequest Receive(ExecuteRemediation message, string key, DateTimeOffset now) =>
        ExecutionRequest.Receive(
            key, message.TenantId, message.EnvironmentId, message.IncidentId,
            message.Plan.PlanId, message.Plan.PlanVersion, message.Plan.PlanHash, message.FencingToken,
            JsonSerializer.Serialize(message.Plan, SentinelJson.Options), now);
}
