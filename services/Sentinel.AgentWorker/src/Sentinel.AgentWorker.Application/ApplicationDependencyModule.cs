using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Sentinel.AgentWorker.Application.Capture;
using Sentinel.AgentWorker.Application.ControlPlane;
using Sentinel.AgentWorker.Application.Diagnostics;
using Sentinel.AgentWorker.Application.Investigations;
using Sentinel.AgentWorker.Application.Options;
using Sentinel.AgentWorker.Application.Tools;

namespace Sentinel.AgentWorker.Application;

/// <summary>
/// AgentWorker's application layer (ADR-020): the building-block pipeline for its processes and tasks,
/// the investigators with their read-only tools, the capture every provider response passes through,
/// and the model telemetry.
/// </summary>
public sealed class ApplicationDependencyModule : IDependencyModule
{
    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddBuildingBlocks(AssemblyReference.Assembly);

        // Every provider response a tool reads goes through the scope's capture: recorded with the
        // execution, and served from that record when the execution is replayed (Phase 07).
        services.AddScoped<InvestigationCapture>();
        services.AddScoped<IInvestigationTool, MetricRangeTool>();
        services.AddScoped<IInvestigationTool, RecentDeploymentsTool>();
        services.AddScoped<IInvestigationTool, LogSignaturesTool>();
        services.AddScoped<IInvestigationTool, SlowSpansTool>();
        services.AddScoped<IInvestigationTool, ServiceDependenciesTool>();
        services.AddScoped<IInvestigationTool, RunbookSearchTool>();
        services.AddScoped<IInvestigator, ModelInvestigator>();
        services.AddScoped<IInvestigator, DeployBlameInvestigator>();
        services.AddScoped<IInvestigator, AdversarialVerifier>();
        services.AddSingleton<AgentWorkerTelemetry>();

        // The control-plane document: published versions over the deployment configuration alone.
        services.AddSingleton(provider =>
        {
            IOptionsFactory<ModelRoutingOptions> routing = provider.GetRequiredService<IOptionsFactory<ModelRoutingOptions>>();
            IOptionsFactory<InvestigationOptions> investigation = provider.GetRequiredService<IOptionsFactory<InvestigationOptions>>();
            IOptionsFactory<ModelPricingOptions> pricing = provider.GetRequiredService<IOptionsFactory<ModelPricingOptions>>();
            return new ModelDeploymentOptions(
                () => routing.Create(ModelDeploymentOptions.OptionsName),
                () => investigation.Create(ModelDeploymentOptions.OptionsName),
                () => pricing.Create(ModelDeploymentOptions.OptionsName));
        });
        services.AddSingleton<VersionedDocumentPublisher<ModelConfigurationDocument>>();
    }
}
