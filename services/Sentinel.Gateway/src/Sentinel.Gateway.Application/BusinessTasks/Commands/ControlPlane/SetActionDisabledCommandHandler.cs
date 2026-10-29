using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.Gateway.Application.Abstractions.Providers;
using Sentinel.Gateway.Application.ControlPlane;

namespace Sentinel.Gateway.Application.BusinessTasks.Commands.ControlPlane;

public sealed class SetActionDisabledCommandHandler(IExecutorControlPlane executor, ControlPlaneAudit audit) : ICommandHandler<SetActionDisabledCommand, ControlPlaneResponse>
{
    public async Task<Result<ControlPlaneResponse>> Handle(SetActionDisabledCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        ControlPlaneResponse response = await executor.SetActionDisabledAsync(command.ActionKind, command.Disabled, command.Actor, cancellationToken);
        string action = command.Disabled ? "controls.action.disable" : "controls.action.enable";
        await audit.RecordAsync(command.Actor, action, $"executor/actions/{command.ActionKind}", new { response.StatusCode, response.Problem }, cancellationToken);
        return Result.Success(response);
    }
}
