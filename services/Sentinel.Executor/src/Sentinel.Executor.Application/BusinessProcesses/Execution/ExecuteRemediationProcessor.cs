using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.Executor.Application.BusinessTasks.Commands.Execution;

namespace Sentinel.Executor.Application.BusinessProcesses.Execution;

public sealed class ExecuteRemediationProcessor(ICommandDispatcher commands) : IProcessor<ExecuteRemediationRequest>
{
    public Task<Result> Process(ExecuteRemediationRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return commands.Dispatch(new ExecuteRemediationCommand(request.Message), cancellationToken);
    }
}