using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Messaging;
using Reference.Scenarios.Application.BusinessTasks.Commands.Runs;

namespace Reference.Scenarios.Application.BusinessProcesses.Runs;

public sealed class StopRunProcessor(ICommandDispatcher commands) : IProcessor<StopRunRequest, bool>
{
    public Task<Result<bool>> Process(StopRunRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return commands.Dispatch(new StopRunCommand(request.RunId), cancellationToken);
    }
}
