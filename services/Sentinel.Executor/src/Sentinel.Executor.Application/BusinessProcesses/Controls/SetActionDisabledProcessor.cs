using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.Executor.Application.BusinessTasks.Commands.Controls;

namespace Sentinel.Executor.Application.BusinessProcesses.Controls;

public sealed class SetActionDisabledProcessor(ICommandDispatcher commands) : IProcessor<SetActionDisabledRequest>
{
    public Task<Result> Process(SetActionDisabledRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return commands.Dispatch(new SetActionDisabledCommand(request.ActionKind, request.Disabled, request.Actor), cancellationToken);
    }
}
