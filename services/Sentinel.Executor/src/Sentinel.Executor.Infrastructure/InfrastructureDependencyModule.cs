using CleanArchitecture.BuildingBlocks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Sentinel.Executor.Infrastructure;

/// <summary>Executor's infrastructure: stores, policy, key resolution, adapters and reconciliation (ADR-020).</summary>
public sealed class InfrastructureDependencyModule : IDependencyModule
{
    public void RegisterServices(IServiceCollection services, IConfiguration configuration) =>
        services.AddExecutorInfrastructure(configuration);
}
