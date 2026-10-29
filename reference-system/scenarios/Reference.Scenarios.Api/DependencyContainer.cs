using CleanArchitecture.BuildingBlocks;
using Reference.Scenarios.Application;

namespace Reference.Scenarios.Api;

/// <summary>The scenario runner's composition (ADR-011, ADR-020): each layer's module, inner layers first.</summary>
internal static class DependencyContainer
{
    public static IServiceCollection RegisterAll(IServiceCollection services, IConfiguration configuration, IHostEnvironment environment) =>
        services.AddModules(
            configuration,
            new ApplicationDependencyModule(),
            new ApiDependencyModule(environment));
}
