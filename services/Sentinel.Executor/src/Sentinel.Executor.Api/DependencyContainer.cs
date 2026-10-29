using CleanArchitecture.BuildingBlocks;
using Sentinel.Executor.Application;
using Sentinel.Executor.Infrastructure;

namespace Sentinel.Executor.Api;

/// <summary>Executor's composition (ADR-020): each layer's module, inner layers first.</summary>
internal static class DependencyContainer
{
    public static IServiceCollection RegisterAll(IServiceCollection services, IConfiguration configuration, IHostEnvironment environment) =>
        services.AddModules(
            configuration,
            new ApplicationDependencyModule(),
            new InfrastructureDependencyModule(),
            new ApiDependencyModule(environment));
}
