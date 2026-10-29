using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.Gateway.Application.BusinessTasks.Commands.ControlPlane;
using Sentinel.Gateway.Application.Abstractions.Providers;

namespace Sentinel.Gateway.Application.BusinessProcesses.ControlPlane;

public sealed class SetActionDisabledProcessor(ICommandDispatcher commands) : IProcessor<SetActionDisabledRequest, ControlPlaneResponse>
{
    public Task<Result<ControlPlaneResponse>> Process(SetActionDisabledRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return commands.Dispatch(new SetActionDisabledCommand(request.ActionKind, request.Disabled, request.Actor), cancellationToken);
    }
}
