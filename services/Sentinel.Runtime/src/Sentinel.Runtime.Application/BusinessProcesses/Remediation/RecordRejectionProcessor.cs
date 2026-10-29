using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.Runtime.Application.BusinessTasks.Commands.Remediation;

namespace Sentinel.Runtime.Application.BusinessProcesses.Remediation;

public sealed class RecordRejectionProcessor(ICommandDispatcher commands) : IProcessor<RecordRejectionRequest>
{
    public Task<Result> Process(RecordRejectionRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return commands.Dispatch(new RecordRejectionCommand(request.IncidentId, request.PlanId, request.PlanVersion, request.RejectedBy, request.Reason), cancellationToken);
    }
}