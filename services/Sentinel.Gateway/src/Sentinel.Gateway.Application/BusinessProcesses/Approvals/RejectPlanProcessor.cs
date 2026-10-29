using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.Gateway.Application.BusinessTasks.Commands.Approvals;

namespace Sentinel.Gateway.Application.BusinessProcesses.Approvals;

public sealed class RejectPlanProcessor(ICommandDispatcher commands) : IProcessor<RejectPlanRequest, ApprovalReceipt>
{
    public Task<Result<ApprovalReceipt>> Process(RejectPlanRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return commands.Dispatch(new RejectPlanCommand(request.IncidentId, request.PlanId, request.PlanVersion, request.Reason, request.ApproverId), cancellationToken);
    }
}
