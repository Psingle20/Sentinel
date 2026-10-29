using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.Runtime.Application.BusinessTasks.Commands.Memory;
using Sentinel.Runtime.Application.Memory;

namespace Sentinel.Runtime.Application.BusinessProcesses.Memory;

public sealed class PurgeIncidentMemoryProcessor(ICommandDispatcher commands) : IProcessor<PurgeIncidentMemoryRequest, PurgeReport>
{
    public Task<Result<PurgeReport>> Process(PurgeIncidentMemoryRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return commands.Dispatch(new PurgeIncidentMemoryCommand(request.DryRun, request.Actor, request.Limit), cancellationToken);
    }
}
