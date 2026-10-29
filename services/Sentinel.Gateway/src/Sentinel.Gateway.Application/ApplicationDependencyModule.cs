using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Sentinel.Gateway.Application.ControlPlane;
using Sentinel.Gateway.Application.Options;

namespace Sentinel.Gateway.Application;

/// <summary>
/// The Gateway's application layer (ADR-020): the building-block pipeline for its processes and tasks,
/// the control-plane audit, and its own control-plane document, tenants and notifications.
/// </summary>
public sealed class ApplicationDependencyModule : IDependencyModule
{
    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddBuildingBlocks(AssemblyReference.Assembly);

        services.AddScoped<ControlPlaneAudit>();

        // The Gateway's own area (Phase 08): published versions over the deployment configuration alone. A
        // webhook may name only an allowed host, and only a secret this deployment holds.
        services.AddSingleton<VersionedDocumentPublisher<ApiConfigurationDocument>>();
        services.AddSingleton(provider =>
        {
            IOptionsFactory<TenantCatalogOptions> tenants = provider.GetRequiredService<IOptionsFactory<TenantCatalogOptions>>();
            IOptionsFactory<NotificationOptions> notifications = provider.GetRequiredService<IOptionsFactory<NotificationOptions>>();
            return new ApiConfigurationService(
                provider.GetRequiredService<VersionedDocumentPublisher<ApiConfigurationDocument>>(),
                () => ApiConfigurationMapping.From(tenants.Create(ApiConfigurationMapping.DeploymentOptions), notifications.Create(ApiConfigurationMapping.DeploymentOptions)),
                [.. notifications.Create(ApiConfigurationMapping.DeploymentOptions).AllowedWebhookHosts],
                key => !string.IsNullOrEmpty(configuration[key]));
        });
    }
}
