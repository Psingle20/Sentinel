using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CleanArchitecture.BuildingBlocks;

/// <summary>
/// What one layer of a service registers: its Application project the building-block pipeline for
/// its own assembly, its Infrastructure project options and port implementations, its Host project
/// transport and identity (ADR-020). Each layer owns one module; Domain has none.
/// </summary>
public interface IDependencyModule
{
    void RegisterServices(IServiceCollection services, IConfiguration configuration);
}
