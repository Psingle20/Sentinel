using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Messaging;
using Reference.Scenarios.Application.BusinessTasks.Commands.Runs;

namespace Reference.Scenarios.Application.BusinessProcesses.Runs;

public sealed class StartRunProcessor(ICommandDispatcher commands) : IProcessor<StartRunRequest, StartRunResult>
{
    public Task<Result<StartRunResult>> Process(StartRunRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return commands.Dispatch(new StartRunCommand(request.ScenarioId), cancellationToken);
    }
}
