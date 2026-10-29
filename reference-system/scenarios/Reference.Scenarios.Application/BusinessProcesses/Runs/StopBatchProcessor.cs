using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Messaging;
using Reference.Scenarios.Application.BusinessTasks.Commands.Runs;

namespace Reference.Scenarios.Application.BusinessProcesses.Runs;

public sealed class StopBatchProcessor(ICommandDispatcher commands) : IProcessor<StopBatchRequest>
{
    public Task<Result> Process(StopBatchRequest request, CancellationToken cancellationToken)
    {
        return commands.Dispatch(new StopBatchCommand(), cancellationToken);
    }
}
