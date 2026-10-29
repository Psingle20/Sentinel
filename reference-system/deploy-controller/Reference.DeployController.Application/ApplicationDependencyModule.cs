using CleanArchitecture.BuildingBlocks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Reference.DeployController.Application.Services;

namespace Reference.DeployController.Application;

/// <summary>The deploy controller's application layer: the building-block pipeline, the ledger it guards and the faults a scenario can inject.</summary>
public sealed class ApplicationDependencyModule : IDependencyModule
{
    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddBuildingBlocks(AssemblyReference.Assembly);
        services.AddSingleton<LedgerState>();
        services.AddSingleton<DeploymentFaults>();
        services.AddScoped<ServiceDescriber>();
    }
}
