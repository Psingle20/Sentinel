using CleanArchitecture.BuildingBlocks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sentinel.Runtime.Infrastructure.DependencyInjection;
using Sentinel.ServiceDefaults;

namespace Sentinel.Runtime.Infrastructure;

/// <summary>Runtime's infrastructure: configuration, stores, grains, workflows, clients, their tracing and the workflow engine's readiness check (ADR-020).</summary>
public sealed class InfrastructureDependencyModule : IDependencyModule
{
    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        services
            .AddRuntimeTracing()
            .AddRuntimeConfiguration(configuration)
            .AddRuntimePersistence()
            .AddRuntimeGrainPorts()
            .AddRuntimeWorkflows(configuration)
            .AddRuntimeClients(configuration);
        services.AddHealthChecks().AddRuntimeWorkflowHealth(ServiceDefaultsExtensions.ReadyTag);
    }
}
