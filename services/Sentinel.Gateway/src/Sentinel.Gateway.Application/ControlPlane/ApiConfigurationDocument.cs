using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Configuration;
using Sentinel.Gateway.Application.Live;
using Sentinel.Contracts;
using Sentinel.Contracts.Incidents;

namespace Sentinel.Gateway.Application.ControlPlane;

/// <summary>
/// Api's control-plane document (Phase 08): the tenant and environment catalog, and notification
/// channels and routes. A webhook channel can target only a host the deployment allows, and names
/// the configuration key of its signing secret rather than the secret, so the document never holds
/// a credential and an operator cannot point Sentinel at an arbitrary address.
/// </summary>
public sealed record ApiConfigurationDocument : IVersionedDocument
{
    public required string Version { get; init; }

    public IReadOnlyList<TenantSetting> Tenants { get; init; } = [];

    public IReadOnlyList<NotificationChannelSetting> Channels { get; init; } = [];

    public IReadOnlyList<NotificationRouteSetting> Routes { get; init; } = [];
}
