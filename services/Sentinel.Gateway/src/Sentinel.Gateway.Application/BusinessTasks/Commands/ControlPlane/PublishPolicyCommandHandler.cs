using System.Text.Json;
using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.Contracts.Remediation;
using Sentinel.Gateway.Application.Abstractions.Providers;
using Sentinel.Gateway.Application.ControlPlane;

namespace Sentinel.Gateway.Application.BusinessTasks.Commands.ControlPlane;

/// <summary>Executor validates and publishes the policy under the operator's signed attestation; the attempt is audited under the version it named.</summary>
public sealed class PublishPolicyCommandHandler(IExecutorControlPlane executor, OperatorAttestations attestations, ControlPlaneAudit audit) : ICommandHandler<PublishPolicyCommand, ControlPlaneResponse>
{
    public async Task<Result<ControlPlaneResponse>> Handle(PublishPolicyCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        string attestation = await attestations.IssueAsync(
            command.Actor, ApprovalAttestation.PublishPolicyScope, "policy",
            OperatorContentHash.Compute(new PolicyPublicationContent(command.Document, command.ExpectedVersion)), cancellationToken);
        ControlPlaneResponse response = await executor.PublishPolicyAsync(command.Document, command.ExpectedVersion, command.Actor, attestation, cancellationToken);
        string version = command.Document.TryGetProperty("version", out JsonElement value) ? value.GetString() ?? "?" : "?";
        await audit.RecordAsync(command.Actor, "policy.publish", $"policy/{version}", new { expectedVersion = command.ExpectedVersion, response.StatusCode, response.Problem }, cancellationToken);
        return Result.Success(response);
    }
}
