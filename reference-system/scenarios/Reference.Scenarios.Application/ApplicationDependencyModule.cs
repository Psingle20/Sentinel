using CleanArchitecture.BuildingBlocks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Reference.Scenarios.Application.Runs;

namespace Reference.Scenarios.Application;

/// <summary>The scenario runner's application layer: the building-block pipeline and the one runner that drives scenario timelines.</summary>
public sealed class ApplicationDependencyModule : IDependencyModule
{
    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddBuildingBlocks(AssemblyReference.Assembly);
        services.AddSingleton<ScenarioRunner>();
    }
}
