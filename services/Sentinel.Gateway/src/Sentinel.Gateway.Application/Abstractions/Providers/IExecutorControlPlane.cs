using System.Text.Json;
using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Integration;

namespace Sentinel.Gateway.Application.Abstractions.Providers;

/// <summary>Executor's policy and operator controls (ADR-006): Api authorizes the operator, Executor owns and validates them.</summary>
public interface IExecutorControlPlane
{
    Task<ControlPlaneResponse> PolicyAsync(CancellationToken cancellationToken);

    Task<ControlPlaneResponse> PolicyHistoryAsync(CancellationToken cancellationToken);

    Task<ControlPlaneResponse> PublishPolicyAsync(JsonElement document, string expectedVersion, string author, CancellationToken cancellationToken);

    Task<ControlPlaneResponse> ControlsAsync(CancellationToken cancellationToken);

    Task<ControlPlaneResponse> SetEmergencyStopAsync(bool engaged, string actor, CancellationToken cancellationToken);

    Task<ControlPlaneResponse> SetActionDisabledAsync(string actionKind, bool disabled, string actor, CancellationToken cancellationToken);
}
