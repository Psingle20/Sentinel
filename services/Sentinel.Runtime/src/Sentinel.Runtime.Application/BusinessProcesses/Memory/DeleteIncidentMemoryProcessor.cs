using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.Runtime.Application.BusinessTasks.Commands.Memory;
using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.Runtime.Application.BusinessProcesses.Memory;

public sealed class DeleteIncidentMemoryProcessor(ICommandDispatcher commands) : IProcessor<DeleteIncidentMemoryRequest>
{
    public Task<Result> Process(DeleteIncidentMemoryRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return commands.Dispatch(new DeleteIncidentMemoryCommand(request.IncidentId, request.Actor, request.Reason), cancellationToken);
    }
}
