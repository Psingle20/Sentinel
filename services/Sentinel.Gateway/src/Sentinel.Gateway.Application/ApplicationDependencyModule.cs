using CleanArchitecture.BuildingBlocks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sentinel.Gateway.Application.Analytics;
using Sentinel.Gateway.Application.ControlPlane;
using Sentinel.Gateway.Application.Postmortems;

namespace Sentinel.Gateway.Application;

/// <summary>The Gateway's application layer: the building-block pipeline for its processes, commands and queries, and the services not yet split into them (ADR-020).</summary>
public sealed class ApplicationDependencyModule : IDependencyModule
{
    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddBuildingBlocks(AssemblyReference.Assembly);

        // Postmortems, the control plane and analytics become processes, commands and queries in the Gateway's own refactoring step.
        services.AddScoped<PostmortemService>();
        services.AddScoped<ControlPlaneService>();
        services.AddSingleton<ProductAnalyticsService>();
    }
}
