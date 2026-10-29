using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.Runtime.Application.BusinessTasks.Commands.Projections;

namespace Sentinel.Runtime.Application.BusinessProcesses.Projections;

public sealed class RebuildProjectionsProcessor(ICommandDispatcher commands) : IProcessor<RebuildProjectionsRequest, int>
{
    public Task<Result<int>> Process(RebuildProjectionsRequest request, CancellationToken cancellationToken)
    {
        return commands.Dispatch(new RebuildProjectionsCommand(), cancellationToken);
    }
}
