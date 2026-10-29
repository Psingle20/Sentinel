using CleanArchitecture.BuildingBlocks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Trace;
using Sentinel.ServiceDefaults;

namespace Sentinel.Runtime.Infrastructure.DependencyInjection;

/// <summary>Runtime's infrastructure: stores, grains, workflows, clients, their tracing and the workflow engine's readiness check (ADR-020).</summary>
public sealed class InfrastructureDependencyModule : IDependencyModule
{
    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        services.ConfigureOpenTelemetryTracerProvider(tracing => tracing.AddSource([.. RuntimeInfrastructure.TraceSources]));
        services.AddRuntimeInfrastructure(configuration);
        services.AddHealthChecks().AddRuntimeHealthChecks(ServiceDefaultsExtensions.ReadyTag);
    }
}