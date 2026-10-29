using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.Executor.Application.BusinessTasks.Commands.Controls;

namespace Sentinel.Executor.Application.BusinessProcesses.Controls;

public sealed class SetEmergencyStopProcessor(ICommandDispatcher commands) : IProcessor<SetEmergencyStopRequest>
{
    public Task<Result> Process(SetEmergencyStopRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return commands.Dispatch(new SetEmergencyStopCommand(request.Engaged, request.Actor), cancellationToken);
    }
}
