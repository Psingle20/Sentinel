using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.Runtime.Application.BusinessTasks.Commands.Remediation;

namespace Sentinel.Runtime.Application.BusinessProcesses.Remediation;

public sealed class RecordApprovalProcessor(ICommandDispatcher commands) : IProcessor<RecordApprovalRequest>
{
    public Task<Result> Process(RecordApprovalRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return commands.Dispatch(new RecordApprovalCommand(request.IncidentId, request.PlanId, request.PlanVersion, request.PlanHash, request.ApproverId, request.Attestation), cancellationToken);
    }
}