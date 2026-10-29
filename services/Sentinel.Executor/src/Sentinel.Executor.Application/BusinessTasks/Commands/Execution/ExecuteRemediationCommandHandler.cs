using System.Text.Json;
using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Integration;
using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.Contracts.Messaging;
using Sentinel.Contracts.Remediation;
using Sentinel.Executor.Domain.Executions;
using Sentinel.Executor.Application.Abstractions.Persistence;
using Sentinel.Executor.Application.Services;

namespace Sentinel.Executor.Application.BusinessTasks.Commands.Execution;

/// <summary>
/// Takes an execution request (ADR-006): a repeated one never runs the action again; a new one is
/// received, its attestation, policy and preconditions are checked, its fencing token must be the
/// highest seen for the incident and its attestation unused, and only then is it reserved and handed
/// to <see cref="ExecutionService"/> to confirm the start with Runtime and execute. Every refusal is
/// final and reported; nothing here retries.
/// </summary>
public sealed class ExecuteRemediationCommandHandler(IExecutionStore store, ExecutionService executions, IUnitOfWork unitOfWork, TimeProvider time) : ICommandHandler<ExecuteRemediationCommand>
{
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
        var request = ExecutionRequest.Receive(
            message.IdempotencyKey, message.TenantId, message.EnvironmentId, message.IncidentId,
            plan.PlanId, plan.PlanVersion, plan.PlanHash, message.FencingToken,
            JsonSerializer.Serialize(plan, SentinelJson.Options), time.GetUtcNow());
        await executions.SaveAsync(request, cancellationToken);

        Result<ApprovalAttestationClaims> approval = await executions.ValidateAsync(message, cancellationToken);
        if (approval.IsFailure)
        {
            request.Reject(approval.Error.Description, time.GetUtcNow());
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
}
