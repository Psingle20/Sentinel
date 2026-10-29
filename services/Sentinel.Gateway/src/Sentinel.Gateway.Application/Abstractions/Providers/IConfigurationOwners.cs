using System.Text.Json;
using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Integration;

namespace Sentinel.Gateway.Application.Abstractions.Providers;

/// <summary>Reads and publishes a control-plane document at the service that owns it.</summary>
public interface IConfigurationOwners
{
    Task<ControlPlaneResponse> CurrentAsync(string area, CancellationToken cancellationToken);

    Task<ControlPlaneResponse> HistoryAsync(string area, CancellationToken cancellationToken);

    Task<ControlPlaneResponse> PublishAsync(string area, JsonElement document, string expectedVersion, string author, CancellationToken cancellationToken);
}
