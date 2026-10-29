using System.Text.Json;
using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Integration;

namespace Sentinel.Gateway.Application.Abstractions.Providers;

/// <summary>
/// Executor's policy and operator controls (ADR-006): the Gateway authenticates and authorizes the
/// operator and signs each change as an operator attestation; Executor verifies it, owns the change
/// and records the actor the attestation names.
/// </summary>
public interface IExecutorControlPlane
{
    Task<ControlPlaneResponse> PolicyAsync(CancellationToken cancellationToken);

    Task<ControlPlaneResponse> PolicyHistoryAsync(CancellationToken cancellationToken);

    Task<ControlPlaneResponse> PublishPolicyAsync(JsonElement document, string expectedVersion, string author, string attestation, CancellationToken cancellationToken);

    Task<ControlPlaneResponse> ControlsAsync(CancellationToken cancellationToken);

    Task<ControlPlaneResponse> SetEmergencyStopAsync(bool engaged, string actor, string attestation, CancellationToken cancellationToken);

    Task<ControlPlaneResponse> SetActionDisabledAsync(string actionKind, bool disabled, string actor, string attestation, CancellationToken cancellationToken);
}
