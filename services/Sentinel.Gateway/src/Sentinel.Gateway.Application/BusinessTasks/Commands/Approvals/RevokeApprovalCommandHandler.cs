using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Integration;
using CleanArchitecture.BuildingBlocks.Messaging;
using Microsoft.Extensions.Logging;
using Sentinel.Gateway.Application.Abstractions.Persistence;
using Sentinel.Gateway.Application.Abstractions.Providers;
using Sentinel.Gateway.Application.Abstractions.Security;
using Sentinel.Gateway.Domain.Approvals;
using Sentinel.Contracts.Incidents;
using Sentinel.Contracts.Remediation;
using Sentinel.Gateway.Application.BusinessProcesses.Approvals;

namespace Sentinel.Gateway.Application.BusinessTasks.Commands.Approvals;

/// <summary>
/// Withdraws an approval before execution starts (Phase 06). Runtime is the authority on whether
/// execution already passed the point of no return, so the Gateway asks it first, under a signed
/// attestation, and records the revocation only once Runtime accepted it (ADR-006). A refusal is
/// reported as a conflict, and an unreachable Runtime leaves the approval standing: an operator
/// never sees a revocation that did not happen.
/// </summary>
public sealed class RevokeApprovalCommandHandler(
    IRuntimeIncidents incidents,
    IRuntimeRemediation runtime,
    IApprovalStore approvals,
    DecisionAttestations attestations,
    IUnitOfWork unitOfWork,
    TimeProvider time,
    ILogger<RevokeApprovalCommandHandler> logger) : ICommandHandler<RevokeApprovalCommand, ApprovalReceipt>
{
    public async Task<Result<ApprovalReceipt>> Handle(RevokeApprovalCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        Approval? approval = await approvals.FindAsync(command.IncidentId, command.PlanId, command.PlanVersion, cancellationToken);
        IncidentDetail? incident = await incidents.GetAsync(command.IncidentId, cancellationToken);
        IncidentState? state = ApprovalMapping.State(incident);
        Result<Approval?> allowed = Approval.CheckRevocation(approval, state, command.Reason);
        if (allowed.IsFailure)
        {
            return Result.Failure<ApprovalReceipt>(allowed.Error);
        }

        if (allowed.Value is { } repeated)
        {
            return Result.Success(ApprovalMapping.Receipt(repeated));
        }

        DateTimeOffset now = time.GetUtcNow();
        (_, SignedAttestation signed) = await attestations.SignAsync(
            ApprovalAttestation.RevokeApprovalScope, state!, new PlanReference(command.PlanId, command.PlanVersion), approval!.PlanHash, command.ApproverId,
            incident!.Plan?.PolicyEvaluation.PolicyVersion ?? string.Empty, now, cancellationToken);

        RevocationResponse answer;
        try
        {
            answer = await runtime.RevokeAsync(
                new RecordRevocation
                {
                    TenantId = approval.TenantId,
                    EnvironmentId = approval.EnvironmentId,
                    IncidentId = command.IncidentId,
                    PlanId = command.PlanId,
                    PlanVersion = command.PlanVersion,
                    RevokedBy = command.ApproverId,
                    Reason = command.Reason,
                    RevokedAt = now,
                    Attestation = signed.Token
                },
                cancellationToken);
        }
        catch (HttpRequestException exception)
        {
            logger.LogWarning(exception, "Runtime could not be reached to revoke approval {ApprovalId} of plan {PlanId} v{Version}", approval.ApprovalId, command.PlanId, command.PlanVersion);
            return Result.Failure<ApprovalReceipt>(ApprovalErrors.RuntimeUnavailable);
        }

        if (!answer.Accepted)
        {
            logger.LogInformation("Runtime refused the revocation of approval {ApprovalId} of plan {PlanId} v{Version}: {Reason}", approval.ApprovalId, command.PlanId, command.PlanVersion, answer.Reason);
            return Result.Failure<ApprovalReceipt>(IsExecutionStarted(answer.Reason) ? ApprovalErrors.ExecutionStarted : ApprovalErrors.RevocationRefused(answer.Reason));
        }

        approval.Revoke(command.ApproverId, command.Reason, now);
        await approvals.SaveAsync(approval, cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);

        logger.LogInformation("{Approver} revoked approval {ApprovalId} of plan {PlanId} v{Version}; Runtime accepted", command.ApproverId, approval.ApprovalId, command.PlanId, command.PlanVersion);
        return Result.Success(ApprovalMapping.Receipt(approval));
    }

    private static bool IsExecutionStarted(string? reason) =>
        reason is not null && reason.Contains("started", StringComparison.OrdinalIgnoreCase);
}
