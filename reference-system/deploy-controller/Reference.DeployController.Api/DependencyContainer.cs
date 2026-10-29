using CleanArchitecture.BuildingBlocks;
using Reference.DeployController.Application;

namespace Reference.DeployController.Api;

/// <summary>The deploy controller's composition (ADR-011, ADR-020): each layer's module, inner layers first.</summary>
internal static class DependencyContainer
{
    public static IServiceCollection RegisterAll(IServiceCollection services, IConfiguration configuration, IHostEnvironment environment) =>
        services.AddModules(
            configuration,
            new ApplicationDependencyModule(),
            new ApiDependencyModule(environment));
}
