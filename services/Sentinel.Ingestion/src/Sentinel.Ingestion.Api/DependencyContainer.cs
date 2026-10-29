using CleanArchitecture.BuildingBlocks;
using Sentinel.Ingestion.Application;
using Sentinel.Ingestion.Infrastructure;

namespace Sentinel.Ingestion.Api;

/// <summary>Ingestion's composition (ADR-020): each layer's module, inner layers first.</summary>
internal static class DependencyContainer
{
    public static IServiceCollection RegisterAll(IServiceCollection services, IConfiguration configuration, IHostEnvironment environment) =>
        services.AddModules(
            configuration,
            new ApplicationDependencyModule(),
            new InfrastructureDependencyModule(),
            new ApiDependencyModule(environment));
}
