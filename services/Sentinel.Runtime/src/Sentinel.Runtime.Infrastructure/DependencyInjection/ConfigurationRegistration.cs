using CleanArchitecture.BuildingBlocks.Configuration;
using CleanArchitecture.BuildingBlocks.Infrastructure.Configuration;
using CleanArchitecture.BuildingBlocks.Infrastructure.PostgreSql;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Sentinel.Contracts.Messaging;
using Sentinel.Runtime.Application.Abstractions.Configuration;
using Sentinel.Runtime.Application.ControlPlane;
using Sentinel.Runtime.Application.Options;
using Sentinel.Runtime.Infrastructure.Configuration;
using Sentinel.Runtime.Infrastructure.Persistence;

namespace Sentinel.Runtime.Infrastructure.DependencyInjection;

internal static class ConfigurationRegistration
{
    /// <summary>
    /// Runtime's options, validated at startup; the deployment configuration alone, which seeds the
    /// control-plane document and is the base a candidate is validated on; and the published document,
    /// stored in Runtime's database and applied over the options (Phase 08).
    /// </summary>
    public static IServiceCollection AddRuntimeConfiguration(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<RuntimeOptions>()
            .Bind(configuration.GetSection(RuntimeOptions.Section))
            .Validate(options => options.IsValid(), "Runtime needs a detection rule with a detection threshold above its onset threshold, known investigation roles and positive workflow timeouts.")
            .ValidateOnStart();
        services.AddOptions<RuntimeOptions>(RuntimeDeploymentOptions.OptionsName).Bind(configuration.GetSection(RuntimeOptions.Section));
        services.AddSingleton<IVersionedDocumentStore<RuntimeConfigurationDocument>>(provider =>
            new PostgresVersionedDocumentStore<RuntimeConfigurationDocument>(provider.GetRequiredService<NpgsqlDataSource>(), RuntimeSchema.ConfigurationTable, SentinelJson.Options));
        services.AddManagedDocument<RuntimeOptions, RuntimeConfigurationDocument>(RuntimeConfigurationMapping.Apply);
        services.AddSingleton<IRuntimeConfigurationActivator, ManagedRuntimeConfigurationActivator>();
        return services;
    }
}
