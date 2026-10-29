using CleanArchitecture.BuildingBlocks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sentinel.AgentWorker.Infrastructure.DependencyInjection;

namespace Sentinel.AgentWorker.Infrastructure;

/// <summary>AgentWorker's infrastructure: configuration, the execution store, Ingestion's clients and the model providers (ADR-020).</summary>
public sealed class InfrastructureDependencyModule : IDependencyModule
{
    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        services
            .AddAgentWorkerConfiguration(configuration)
            .AddAgentWorkerPersistence()
            .AddAgentWorkerClients(configuration)
            .AddAgentWorkerModels(configuration);
    }
}
