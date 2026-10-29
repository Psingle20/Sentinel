using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.Contracts.Remediation;
using Sentinel.Gateway.Application.Abstractions.Providers;
using Sentinel.Gateway.Application.ControlPlane;

namespace Sentinel.Gateway.Application.BusinessTasks.Commands.ControlPlane;

public sealed class SetActionDisabledCommandHandler(IExecutorControlPlane executor, OperatorAttestations attestations, ControlPlaneAudit audit) : ICommandHandler<SetActionDisabledCommand, ControlPlaneResponse>
{
    public static string Resource(string actionKind) => $"controls/actions/{actionKind}";

    public async Task<Result<ControlPlaneResponse>> Handle(SetActionDisabledCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        string resource = Resource(command.ActionKind);
        string attestation = await attestations.IssueAsync(
            command.Actor, ApprovalAttestation.SetControlScope, resource, OperatorContentHash.Compute(new ControlChangeContent(resource, command.Disabled)), cancellationToken);
        ControlPlaneResponse response = await executor.SetActionDisabledAsync(command.ActionKind, command.Disabled, command.Actor, attestation, cancellationToken);
        string action = command.Disabled ? "controls.action.disable" : "controls.action.enable";
        await audit.RecordAsync(command.Actor, action, $"executor/actions/{command.ActionKind}", new { response.StatusCode, response.Problem }, cancellationToken);
        return Result.Success(response);
    }
}
