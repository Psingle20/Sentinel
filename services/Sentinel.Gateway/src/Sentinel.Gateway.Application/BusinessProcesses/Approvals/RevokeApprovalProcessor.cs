using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.Gateway.Application.BusinessTasks.Commands.Approvals;

namespace Sentinel.Gateway.Application.BusinessProcesses.Approvals;

public sealed class RevokeApprovalProcessor(ICommandDispatcher commands) : IProcessor<RevokeApprovalRequest, ApprovalReceipt>
{
    public Task<Result<ApprovalReceipt>> Process(RevokeApprovalRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return commands.Dispatch(new RevokeApprovalCommand(request.IncidentId, request.PlanId, request.PlanVersion, request.Reason, request.ApproverId), cancellationToken);
    }
}
