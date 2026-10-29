using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Integration;
using CleanArchitecture.BuildingBlocks.Messaging;
using Microsoft.Extensions.Logging;
using Sentinel.Gateway.Application.Abstractions.Persistence;
using Sentinel.Gateway.Application.Abstractions.Providers;
using Sentinel.Gateway.Domain.Approvals;
using Sentinel.Contracts.Incidents;
using Sentinel.Contracts.Remediation;
using Sentinel.Gateway.Application.BusinessProcesses.Approvals;

namespace Sentinel.Gateway.Application.Commands.Approvals;

/// <summary>
/// Withdraws an approval before execution starts (Phase 06): the Gateway marks it revoked and tells
/// Runtime, which refuses any start confirmation, so the attestation can never take effect.
/// </summary>
public sealed class RevokeApprovalCommandHandler(
    IRuntimeIncidents incidents,
    IApprovalStore approvals,
    IOutbox outbox,
    IUnitOfWork unitOfWork,
    TimeProvider time,
    ILogger<RevokeApprovalCommandHandler> logger) : ICommandHandler<RevokeApprovalCommand, ApprovalReceipt>
{
    public async Task<Result<ApprovalReceipt>> Handle(RevokeApprovalCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        Approval? approval = await approvals.FindAsync(command.IncidentId, command.PlanId, command.PlanVersion, cancellationToken);
        IncidentDetail? incident = await incidents.GetAsync(command.IncidentId, cancellationToken);
        Result<Approval?> allowed = Approval.CheckRevocation(approval, ApprovalMapping.State(incident), command.Reason);
        if (allowed.IsFailure)
        {
            return Result.Failure<ApprovalReceipt>(allowed.Error);
        }

        if (allowed.Value is { } repeated)
        {
            return Result.Success(ApprovalMapping.Receipt(repeated));
        }

        DateTimeOffset now = time.GetUtcNow();
        approval!.Revoke(command.ApproverId, command.Reason, now);
        await approvals.SaveAsync(approval, cancellationToken);
        outbox.Enqueue(
            new RecordRevocation
            {
                TenantId = approval.TenantId,
                EnvironmentId = approval.EnvironmentId,
                IncidentId = command.IncidentId,
                PlanId = command.PlanId,
                PlanVersion = command.PlanVersion,
                RevokedBy = command.ApproverId,
                Reason = command.Reason,
                RevokedAt = now
            },
            ApprovalMapping.Metadata(incident!));
        await unitOfWork.CommitAsync(cancellationToken);

        logger.LogInformation("{Approver} revoked approval {ApprovalId} of plan {PlanId} v{Version}", command.ApproverId, approval.ApprovalId, command.PlanId, command.PlanVersion);
        return Result.Success(ApprovalMapping.Receipt(approval));
    }
}