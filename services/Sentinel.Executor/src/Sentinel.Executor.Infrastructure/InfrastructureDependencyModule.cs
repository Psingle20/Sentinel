using CleanArchitecture.BuildingBlocks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sentinel.Executor.Infrastructure.DependencyInjection;
using Sentinel.Executor.Infrastructure.Execution;

namespace Sentinel.Executor.Infrastructure;

/// <summary>Executor's infrastructure: its database, the Gateway and Runtime clients, the privileged adapters and reconciliation (ADR-020).</summary>
public sealed class InfrastructureDependencyModule : IDependencyModule
{
    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        services
            .AddExecutorPersistence(configuration)
            .AddExecutorClients(configuration)
            .AddRemediationAdapters(configuration)
            .AddHostedService<ReconciliationWorker>();
    }
}
