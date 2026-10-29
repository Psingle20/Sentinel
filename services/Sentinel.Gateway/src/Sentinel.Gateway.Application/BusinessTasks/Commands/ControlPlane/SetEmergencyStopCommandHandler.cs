using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.Contracts.Remediation;
using Sentinel.Gateway.Application.Abstractions.Providers;
using Sentinel.Gateway.Application.ControlPlane;

namespace Sentinel.Gateway.Application.BusinessTasks.Commands.ControlPlane;

public sealed class SetEmergencyStopCommandHandler(IExecutorControlPlane executor, OperatorAttestations attestations, ControlPlaneAudit audit) : ICommandHandler<SetEmergencyStopCommand, ControlPlaneResponse>
{
    public const string Resource = "controls/emergency-stop";

    public async Task<Result<ControlPlaneResponse>> Handle(SetEmergencyStopCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        string attestation = await attestations.IssueAsync(
            command.Actor, ApprovalAttestation.SetControlScope, Resource, OperatorContentHash.Compute(new ControlChangeContent(Resource, command.Engaged)), cancellationToken);
        ControlPlaneResponse response = await executor.SetEmergencyStopAsync(command.Engaged, command.Actor, attestation, cancellationToken);
        string action = command.Engaged ? "controls.emergency-stop.engage" : "controls.emergency-stop.release";
        await audit.RecordAsync(command.Actor, action, "executor/emergency-stop", new { response.StatusCode, response.Problem }, cancellationToken);
        return Result.Success(response);
    }
}
