using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.Executor.Application.BusinessTasks.Commands.Execution;

namespace Sentinel.Executor.Application.BusinessProcesses.Execution;

public sealed class ReconcileExecutionProcessor(ICommandDispatcher commands) : IProcessor<ReconcileExecutionRequest>
{
    public Task<Result> Process(ReconcileExecutionRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return commands.Dispatch(new ReconcileExecutionCommand(request.IdempotencyKey), cancellationToken);
    }
}
