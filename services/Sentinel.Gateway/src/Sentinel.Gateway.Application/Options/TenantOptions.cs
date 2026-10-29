using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Configuration;
using Sentinel.Gateway.Application.Live;
using Sentinel.Contracts;
using Sentinel.Contracts.Incidents;

namespace Sentinel.Gateway.Application.Options;

public sealed class TenantOptions
{
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public IList<string> Environments { get; } = [];
}
