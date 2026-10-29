using CleanArchitecture.BuildingBlocks.Configuration;
using CleanArchitecture.BuildingBlocks.Infrastructure.Configuration;
using CleanArchitecture.BuildingBlocks.Infrastructure.PostgreSql;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using Sentinel.AgentWorker.Application.Abstractions.Configuration;
using Sentinel.AgentWorker.Application.ControlPlane;
using Sentinel.AgentWorker.Application.Options;
using Sentinel.AgentWorker.Infrastructure.Configuration;
using Sentinel.AgentWorker.Infrastructure.Persistence;
using Sentinel.Contracts.Messaging;

namespace Sentinel.AgentWorker.Infrastructure.DependencyInjection;

internal static class ConfigurationRegistration
{
    /// <summary>
    /// AgentWorker's options. Each one the control plane manages is also bound as the deployment
    /// configuration alone, the control-plane document's seed; the published document is stored in
    /// AgentWorker's database and applied over the options (Phase 08 model selection).
    /// </summary>
    public static IServiceCollection AddAgentWorkerConfiguration(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<InvestigationOptions>().Bind(configuration.GetSection(InvestigationOptions.Section));
        services.AddOptions<InvestigationOptions>(ModelDeploymentOptions.OptionsName).Bind(configuration.GetSection(InvestigationOptions.Section));
        services.AddOptions<ModelRoutingOptions>().Bind(configuration.GetSection(ModelRoutingOptions.Section));
        services.AddOptions<ModelRoutingOptions>(ModelDeploymentOptions.OptionsName).Bind(configuration.GetSection(ModelRoutingOptions.Section));
        services.AddOptions<ModelPricingOptions>().Bind(configuration.GetSection(ModelPricingOptions.Section));
        services.AddOptions<ModelPricingOptions>(ModelDeploymentOptions.OptionsName).Bind(configuration.GetSection(ModelPricingOptions.Section));
        services.AddOptions<CompletenessOptions>().Bind(configuration.GetSection(CompletenessOptions.Section));
        services.TryAddSingleton(TimeProvider.System);

        services.AddSingleton<IVersionedDocumentStore<ModelConfigurationDocument>>(provider =>
            new PostgresVersionedDocumentStore<ModelConfigurationDocument>(provider.GetRequiredService<NpgsqlDataSource>(), AgentWorkerSchema.ConfigurationTable, SentinelJson.Options));
        services.AddManagedDocument<ModelRoutingOptions, ModelConfigurationDocument>(ModelConfigurationMapping.Apply);
        services.AddManagedDocument<InvestigationOptions, ModelConfigurationDocument>(ModelConfigurationMapping.Apply);
        services.AddManagedDocument<ModelPricingOptions, ModelConfigurationDocument>(ModelConfigurationMapping.Apply);
        services.AddSingleton<IModelConfigurationActivator, ManagedModelConfigurationActivator>();
        return services;
    }
}
