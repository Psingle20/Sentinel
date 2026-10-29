using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Sentinel.Ingestion.Application.BusinessTasks.Queries;
using Sentinel.Ingestion.Application.ControlPlane;
using Sentinel.Ingestion.Application.Options;
using Sentinel.Ingestion.Application.Services;

namespace Sentinel.Ingestion.Application;

/// <summary>
/// Ingestion's application layer (ADR-020): the building-block pipeline for its processes and tasks,
/// the pull the ingestion worker runs per source, and the bulkhead its queries share.
/// </summary>
public sealed class ApplicationDependencyModule : IDependencyModule
{
    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddBuildingBlocks(AssemblyReference.Assembly);

        services.AddScoped<IngestionCycle>();
        services.AddSingleton<QueryBulkhead>();

        // The control-plane document: published versions over the deployment configuration alone.
        services.AddSingleton(provider => new IngestionDeploymentOptions(() => provider.GetRequiredService<IOptionsFactory<IngestionOptions>>().Create(IngestionDeploymentOptions.OptionsName)));
        services.AddSingleton<VersionedDocumentPublisher<SourceConfigurationDocument>>();
    }
}
