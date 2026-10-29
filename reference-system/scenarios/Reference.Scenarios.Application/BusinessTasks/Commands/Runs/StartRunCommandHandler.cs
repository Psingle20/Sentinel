using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Messaging;
using Reference.Scenarios.Application.BusinessProcesses.Runs;
using Reference.Scenarios.Application.Runs;

namespace Reference.Scenarios.Application.BusinessTasks.Commands.Runs;

public sealed class StartRunCommandHandler(ScenarioRunner runner) : ICommandHandler<StartRunCommand, StartRunResult>
{
    public Task<Result<StartRunResult>> Handle(StartRunCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        (StartRunOutcome outcome, ScenarioRunView? run) = runner.Start(command.ScenarioId);
        return Task.FromResult(Result.Success(new StartRunResult(outcome, run)));
    }
}
