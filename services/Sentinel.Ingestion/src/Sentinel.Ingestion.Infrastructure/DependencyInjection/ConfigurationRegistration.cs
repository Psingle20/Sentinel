using CleanArchitecture.BuildingBlocks.Configuration;
using CleanArchitecture.BuildingBlocks.Infrastructure.Configuration;
using CleanArchitecture.BuildingBlocks.Infrastructure.PostgreSql;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using Sentinel.Contracts.Messaging;
using Sentinel.Ingestion.Application.Abstractions.Configuration;
using Sentinel.Ingestion.Application.ControlPlane;
using Sentinel.Ingestion.Application.Options;
using Sentinel.Ingestion.Infrastructure.Configuration;
using Sentinel.Ingestion.Infrastructure.Persistence;

namespace Sentinel.Ingestion.Infrastructure.DependencyInjection;

internal static class ConfigurationRegistration
{
    /// <summary>
    /// Ingestion's options, validated at startup; the deployment configuration alone, which seeds the
    /// control-plane document and is the base a candidate is validated on; and the published document,
    /// stored in Ingestion's database and applied over the options (Phase 08 provider configuration).
    /// </summary>
    public static IServiceCollection AddIngestionConfiguration(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<IngestionOptions>()
            .Bind(configuration.GetSection(IngestionOptions.Section))
            .Validate(options => options.IsValid(), "Ingestion options need a tenant, environment, positive intervals and complete metric series.")
            .ValidateOnStart();
        services.TryAddSingleton(TimeProvider.System);

        services.AddOptions<IngestionOptions>(IngestionDeploymentOptions.OptionsName).Bind(configuration.GetSection(IngestionOptions.Section));
        services.AddSingleton<IVersionedDocumentStore<SourceConfigurationDocument>>(provider =>
            new PostgresVersionedDocumentStore<SourceConfigurationDocument>(provider.GetRequiredService<NpgsqlDataSource>(), IngestionStoreInitializer.ConfigurationTable, SentinelJson.Options));
        services.AddManagedDocument<IngestionOptions, SourceConfigurationDocument>(SourceConfigurationMapping.Apply);
        services.AddSingleton<ISourceConfigurationActivator, ManagedSourceConfigurationActivator>();
        return services;
    }
}
