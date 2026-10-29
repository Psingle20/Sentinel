using CleanArchitecture.BuildingBlocks.Configuration;
using CleanArchitecture.BuildingBlocks.Infrastructure.Configuration;
using CleanArchitecture.BuildingBlocks.Infrastructure.PostgreSql;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using Sentinel.Contracts.Messaging;
using Sentinel.Gateway.Application.ControlPlane;
using Sentinel.Gateway.Application.Options;
using Sentinel.Gateway.Infrastructure.Persistence;

namespace Sentinel.Gateway.Infrastructure.DependencyInjection;

internal static class ConfigurationRegistration
{
    /// <summary>
    /// The tenant catalog and notification options, each also bound as the deployment configuration
    /// alone, the seed of the Gateway's own control-plane document; the published document is stored in
    /// the Gateway's database and applied over the options (Phase 08).
    /// </summary>
    public static IServiceCollection AddGatewayConfiguration(this IServiceCollection services, IConfiguration configuration)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddOptions<TenantCatalogOptions>().Bind(configuration.GetSection(TenantCatalogOptions.Section));
        services.AddOptions<TenantCatalogOptions>(ApiConfigurationMapping.DeploymentOptions).Bind(configuration.GetSection(TenantCatalogOptions.Section));
        services.AddOptions<NotificationOptions>().Bind(configuration.GetSection(NotificationOptions.Section));
        services.AddOptions<NotificationOptions>(ApiConfigurationMapping.DeploymentOptions).Bind(configuration.GetSection(NotificationOptions.Section));
        services.AddSingleton<IVersionedDocumentStore<ApiConfigurationDocument>>(provider =>
            new PostgresVersionedDocumentStore<ApiConfigurationDocument>(provider.GetRequiredService<NpgsqlDataSource>(), ApiSchema.ConfigurationTable, SentinelJson.Options));
        services.AddManagedDocument<TenantCatalogOptions, ApiConfigurationDocument>(ApiConfigurationMapping.Apply);
        services.AddManagedDocument<NotificationOptions, ApiConfigurationDocument>(ApiConfigurationMapping.Apply);
        return services;
    }
}
