using System.Text.Json;
using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.Gateway.Application.Abstractions.Providers;
using Sentinel.Gateway.Application.ControlPlane;

namespace Sentinel.Gateway.Application.BusinessTasks.Commands.ControlPlane;

/// <summary>The area's owner validates and publishes the document; the attempt is audited, and an unknown area reaches no service.</summary>
public sealed class PublishConfigurationCommandHandler(IConfigurationOwners owners, ControlPlaneAudit audit) : ICommandHandler<PublishConfigurationCommand, ControlPlaneResponse>
{
    public async Task<Result<ControlPlaneResponse>> Handle(PublishConfigurationCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (!ConfigurationAreas.All.Contains(command.Area))
        {
            return Result.Success(ConfigurationAreas.Unknown);
        }

        ControlPlaneResponse response = await owners.PublishAsync(command.Area, command.Document, command.ExpectedVersion, command.Actor, cancellationToken);
        JsonElement document = command.Document;
        string version = document.ValueKind == JsonValueKind.Object && document.TryGetProperty("version", out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString()! : "?";
        await audit.RecordAsync(command.Actor, "configuration.publish", $"{command.Area}/{version}", new { expectedVersion = command.ExpectedVersion, response.StatusCode, response.Problem }, cancellationToken);
        return Result.Success(response);
    }
}
