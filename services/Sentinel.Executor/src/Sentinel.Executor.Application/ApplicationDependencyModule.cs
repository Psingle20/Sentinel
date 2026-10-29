using CleanArchitecture.BuildingBlocks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Sentinel.Executor.Application;

/// <summary>Executor's application layer: the building-block pipeline for its processes and tasks, and the services not yet split into them (ADR-020).</summary>
public sealed class ApplicationDependencyModule : IDependencyModule
{
    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddBuildingBlocks(AssemblyReference.Assembly);

        // Policy evaluation and administration, verification and the execution state machine become processes and tasks in Executor's own step.
        services.AddScoped<PolicyService>();
        services.AddScoped<PolicyAdministration>();
        services.AddScoped<AttestationVerifier>();
        services.AddScoped<ExecutionService>();
    }
}