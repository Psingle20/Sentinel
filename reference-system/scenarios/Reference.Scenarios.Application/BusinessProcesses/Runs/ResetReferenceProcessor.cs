using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Messaging;
using Reference.Scenarios.Application.BusinessTasks.Commands.Runs;

namespace Reference.Scenarios.Application.BusinessProcesses.Runs;

public sealed class ResetReferenceProcessor(ICommandDispatcher commands) : IProcessor<ResetReferenceRequest>
{
    public Task<Result> Process(ResetReferenceRequest request, CancellationToken cancellationToken)
    {
        return commands.Dispatch(new ResetReferenceCommand(), cancellationToken);
    }
}
