using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.Gateway.Application.BusinessTasks.Commands.Approvals;

namespace Sentinel.Gateway.Application.BusinessProcesses.Approvals;

public sealed class ApprovePlanProcessor(ICommandDispatcher commands) : IProcessor<ApprovePlanRequest, ApprovalReceipt>
{
    public Task<Result<ApprovalReceipt>> Process(ApprovePlanRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return commands.Dispatch(new ApprovePlanCommand(request.IncidentId, request.PlanId, request.PlanVersion, request.PlanHash, request.ApproverId), cancellationToken);
    }
}
