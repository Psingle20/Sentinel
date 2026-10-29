using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.Runtime.Application.BusinessTasks.Commands.Memory;
using Sentinel.Contracts.Incidents;
using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.Runtime.Application.BusinessProcesses.Memory;

public sealed class SetLegalHoldProcessor(ICommandDispatcher commands) : IProcessor<SetLegalHoldRequest, IncidentMemoryView?>
{
    public Task<Result<IncidentMemoryView?>> Process(SetLegalHoldRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return commands.Dispatch(new SetLegalHoldCommand(request.IncidentId, request.Hold, request.Actor), cancellationToken);
    }
}
