using CleanArchitecture.BuildingBlocks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sentinel.Ingestion.Infrastructure.DependencyInjection;

namespace Sentinel.Ingestion.Infrastructure;

/// <summary>Ingestion's infrastructure: configuration, stores, provider adapters and the ingestion worker (ADR-020).</summary>
public sealed class InfrastructureDependencyModule : IDependencyModule
{
    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        services
            .AddIngestionConfiguration(configuration)
            .AddIngestionPersistence()
            .AddIngestionProviders(configuration)
            .AddIngestionWorker();
    }
}
