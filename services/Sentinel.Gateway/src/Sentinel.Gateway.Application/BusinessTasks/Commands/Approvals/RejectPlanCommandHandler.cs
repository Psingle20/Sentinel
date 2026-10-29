using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Integration;
using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.Gateway.Application.Abstractions.Persistence;
using Sentinel.Gateway.Application.Abstractions.Providers;
using Sentinel.Gateway.Domain.Approvals;
using Sentinel.Contracts.Incidents;
using Sentinel.Contracts.Remediation;
using Sentinel.Gateway.Application.BusinessProcesses.Approvals;

namespace Sentinel.Gateway.Application.BusinessTasks.Commands.Approvals;

/// <summary>Records a rejection of the incident's current plan and relays it to Runtime.</summary>
public sealed class RejectPlanCommandHandler(
    IRuntimeIncidents incidents,
    IApprovalStore approvals,
    IOutbox outbox,
    IUnitOfWork unitOfWork,
    TimeProvider time) : ICommandHandler<RejectPlanCommand, ApprovalReceipt>
{
    public async Task<Result<ApprovalReceipt>> Handle(RejectPlanCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        IncidentDetail? incident = await incidents.GetAsync(command.IncidentId, cancellationToken);
        IncidentState? state = ApprovalMapping.State(incident);
        Approval? existing = await approvals.FindAsync(command.IncidentId, command.PlanId, command.PlanVersion, cancellationToken);
        Result<Approval?> allowed = Approval.CheckRejection(existing, state, new PlanReference(command.PlanId, command.PlanVersion), command.Reason);
        if (allowed.IsFailure)
        {
            return Result.Failure<ApprovalReceipt>(allowed.Error);
        }

        if (allowed.Value is { } repeated)
        {
            return Result.Success(ApprovalMapping.Receipt(repeated));
        }

        DateTimeOffset now = time.GetUtcNow();
        var rejection = Approval.Reject(state!, command.ApproverId, command.Reason, now);
        await approvals.SaveAsync(rejection, cancellationToken);
        outbox.Enqueue(
            new RecordRejection
            {
                TenantId = rejection.TenantId,
                EnvironmentId = rejection.EnvironmentId,
                IncidentId = command.IncidentId,
                PlanId = command.PlanId,
                PlanVersion = command.PlanVersion,
                RejectedBy = command.ApproverId,
                Reason = command.Reason,
                RejectedAt = now
            },
            ApprovalMapping.Metadata(incident!));
        await unitOfWork.CommitAsync(cancellationToken);
        return Result.Success(ApprovalMapping.Receipt(rejection));
    }
}
