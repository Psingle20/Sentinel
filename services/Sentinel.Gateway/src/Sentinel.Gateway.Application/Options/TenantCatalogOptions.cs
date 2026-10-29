using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Configuration;
using Sentinel.Gateway.Application.Live;
using Sentinel.Contracts;
using Sentinel.Contracts.Incidents;

namespace Sentinel.Gateway.Application.Options;

/// <summary>Configured as <c>Gateway:Tenants</c>: the tenants Sentinel serves and their environments (Phase 08 tenant management).</summary>
public sealed class TenantCatalogOptions
{
    public const string Section = "Gateway:Tenants";

    public IList<TenantOptions> Tenants { get; } = [];
}
