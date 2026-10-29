using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.Gateway.Application.Abstractions.Providers;
using Sentinel.Gateway.Application.ControlPlane;

namespace Sentinel.Gateway.Application.BusinessTasks.Commands.ControlPlane;

public sealed class SetEmergencyStopCommandHandler(IExecutorControlPlane executor, ControlPlaneAudit audit) : ICommandHandler<SetEmergencyStopCommand, ControlPlaneResponse>
{
    public async Task<Result<ControlPlaneResponse>> Handle(SetEmergencyStopCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        ControlPlaneResponse response = await executor.SetEmergencyStopAsync(command.Engaged, command.Actor, cancellationToken);
        string action = command.Engaged ? "controls.emergency-stop.engage" : "controls.emergency-stop.release";
        await audit.RecordAsync(command.Actor, action, "executor/emergency-stop", new { response.StatusCode, response.Problem }, cancellationToken);
        return Result.Success(response);
    }
}
