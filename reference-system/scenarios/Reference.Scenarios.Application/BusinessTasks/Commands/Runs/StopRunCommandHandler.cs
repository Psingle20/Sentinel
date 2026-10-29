using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Messaging;
using Reference.Scenarios.Application.Runs;

namespace Reference.Scenarios.Application.BusinessTasks.Commands.Runs;

/// <summary>Whether the run is known: a known run stops between steps, or has already finished.</summary>
public sealed class StopRunCommandHandler(ScenarioRunner runner) : ICommandHandler<StopRunCommand, bool>
{
    public Task<Result<bool>> Handle(StopRunCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        return Task.FromResult(Result.Success(runner.Stop(command.RunId)));
    }
}
