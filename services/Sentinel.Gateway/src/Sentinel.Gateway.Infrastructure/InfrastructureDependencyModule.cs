using CleanArchitecture.BuildingBlocks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sentinel.Gateway.Infrastructure.DependencyInjection;

namespace Sentinel.Gateway.Infrastructure;

/// <summary>The Gateway's infrastructure: approvals and signing, configuration, stores, peer clients and notifications (ADR-020).</summary>
public sealed class InfrastructureDependencyModule : IDependencyModule
{
    public void RegisterServices(IServiceCollection services, IConfiguration configuration) =>
        services
            .AddApprovals(configuration)
            .AddGatewayConfiguration(configuration)
            .AddGatewayPersistence()
            .AddGatewayClients(configuration)
            .AddGatewayNotifications();
}
