using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.Gateway.Application.BusinessTasks.Commands.ControlPlane;
using Sentinel.Gateway.Application.Abstractions.Providers;

namespace Sentinel.Gateway.Application.BusinessProcesses.ControlPlane;

public sealed class SetEmergencyStopProcessor(ICommandDispatcher commands) : IProcessor<SetEmergencyStopRequest, ControlPlaneResponse>
{
    public Task<Result<ControlPlaneResponse>> Process(SetEmergencyStopRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return commands.Dispatch(new SetEmergencyStopCommand(request.Engaged, request.Actor), cancellationToken);
    }
}
