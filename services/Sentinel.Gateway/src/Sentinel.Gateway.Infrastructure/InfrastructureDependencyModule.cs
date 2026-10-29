using CleanArchitecture.BuildingBlocks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sentinel.Gateway.Infrastructure.DependencyInjection;

namespace Sentinel.Gateway.Infrastructure;

/// <summary>The Gateway's infrastructure: port implementations, options, clients and background work (ADR-020).</summary>
public sealed class InfrastructureDependencyModule : IDependencyModule
{
    public void RegisterServices(IServiceCollection services, IConfiguration configuration) =>
        services
            .AddApprovals(configuration)
            .AddApiInfrastructure(configuration);
}
