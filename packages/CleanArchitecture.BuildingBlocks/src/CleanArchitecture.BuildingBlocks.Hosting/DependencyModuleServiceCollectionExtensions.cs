using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CleanArchitecture.BuildingBlocks;

public static class DependencyModuleServiceCollectionExtensions
{
    /// <summary>
    /// Registers <paramref name="modules"/> in the order given. A host's dependency container passes
    /// its layers' modules inner first, Application, Infrastructure, Host, so an outer layer's
    /// replacement or decoration sees the registrations it refines (ADR-020).
    /// </summary>
    public static IServiceCollection AddModules(this IServiceCollection services, IConfiguration configuration, params IDependencyModule[] modules)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(modules);

        foreach (IDependencyModule module in modules)
        {
            ArgumentNullException.ThrowIfNull(module);
            module.RegisterServices(services, configuration);
        }

        return services;
    }
}
