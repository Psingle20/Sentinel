using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sentinel.Runtime.Application.ControlPlane;
using Sentinel.Runtime.Application.Incidents;
using Sentinel.Runtime.Application.Memory;
using Sentinel.Runtime.Application.Services;

namespace Sentinel.Runtime.Application;

/// <summary>Runtime's application layer: the building-block pipeline for its processes and tasks, and the services not yet split into them (ADR-020).</summary>
public sealed class ApplicationDependencyModule : IDependencyModule
{
    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddBuildingBlocks(AssemblyReference.Assembly);
        services.AddScoped<IncidentReportRecorder>();

        // Incident queries, remediation control, memory and replay become processes and tasks in Runtime's own refactoring step.
        services.AddScoped<IncidentCommandExecutor>();
        services.AddScoped<IncidentSteps>();
        services.AddScoped<RecoveryAssessor>();
        services.AddScoped<DetectionService>();
        services.AddScoped<RemediationControlService>();
        services.AddScoped<IncidentQueryService>();
        services.AddScoped<ProjectionRebuilder>();
        services.AddScoped<IncidentMemoryRecorder>();
        services.AddScoped<IncidentMemoryService>();
        services.AddScoped<DomainReplayService>();
        services.AddScoped<PostmortemService>();
        services.AddSingleton<ServiceTopologyCache>();
        services.AddSingleton<VersionedDocumentPublisher<RuntimeConfigurationDocument>>();
        services.AddSingleton<RuntimeConfigurationService>();
    }
}
