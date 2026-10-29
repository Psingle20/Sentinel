using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Sentinel.Runtime.Application.ControlPlane;
using Sentinel.Runtime.Application.Detection;
using Sentinel.Runtime.Application.Incidents;
using Sentinel.Runtime.Application.Memory;
using Sentinel.Runtime.Application.Options;
using Sentinel.Runtime.Application.Recovery;
using Sentinel.Runtime.Application.Services;

namespace Sentinel.Runtime.Application;

/// <summary>
/// Runtime's application layer (ADR-020): the building-block pipeline for its processes and tasks,
/// and the application services the incident's writer, workflow steps and correlation grains use.
/// </summary>
public sealed class ApplicationDependencyModule : IDependencyModule
{
    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddBuildingBlocks(AssemblyReference.Assembly);

        services.AddScoped<IncidentReportRecorder>();
        services.AddScoped<DecisionVerifier>();
        services.AddScoped<IncidentCommandExecutor>();
        services.AddScoped<IncidentSteps>();
        services.AddScoped<RecoveryAssessor>();
        services.AddScoped<DetectionService>();
        services.AddScoped<IncidentMemoryRecorder>();
        services.AddScoped<IncidentSimilarity>();
        services.AddSingleton<ServiceTopologyCache>();

        // The control-plane document: published versions over the deployment configuration alone.
        services.AddSingleton(provider => new RuntimeDeploymentOptions(() => provider.GetRequiredService<IOptionsFactory<RuntimeOptions>>().Create(RuntimeDeploymentOptions.OptionsName)));
        services.AddSingleton<VersionedDocumentPublisher<RuntimeConfigurationDocument>>();
    }
}
