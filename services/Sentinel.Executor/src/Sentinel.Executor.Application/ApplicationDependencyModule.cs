using CleanArchitecture.BuildingBlocks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sentinel.Executor.Application.Services;

namespace Sentinel.Executor.Application;

/// <summary>
/// Executor's application layer (ADR-020): the building-block pipeline for its processes and tasks,
/// and the policy engine, attestation verification and execution state machine they share.
/// </summary>
public sealed class ApplicationDependencyModule : IDependencyModule
{
    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddBuildingBlocks(AssemblyReference.Assembly);

        services.AddScoped<PolicyService>();
        services.AddScoped<AttestationVerifier>();
        services.AddScoped<ExecutionService>();
    }
}
