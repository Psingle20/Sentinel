using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.Runtime.Application.BusinessTasks.Commands.Remediation;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Runtime.Domain.Incidents;

namespace Sentinel.Runtime.Application.BusinessProcesses.Remediation;

public sealed class RecordExecutionResultProcessor(ICommandDispatcher commands) : IProcessor<RecordExecutionResultRequest>
{
    public Task<Result> Process(RecordExecutionResultRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return commands.Dispatch(new RecordExecutionResultCommand(request.IncidentId, request.PlanId, request.PlanVersion, request.Result, request.Detail), cancellationToken);
    }
}
