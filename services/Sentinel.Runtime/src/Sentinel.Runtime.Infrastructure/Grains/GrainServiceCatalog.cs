using CleanArchitecture.BuildingBlocks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sentinel.Contracts.Observations;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Runtime.Application;
using Sentinel.Runtime.Application.Incidents;
using Sentinel.Runtime.Application.Abstractions.Runtime;

namespace Sentinel.Runtime.Infrastructure.Grains;

/// <summary><see cref="IServiceCatalog"/> backed by ServiceGrain.</summary>
internal sealed class GrainServiceCatalog(IGrainFactory grains) : IServiceCatalog
{
    public Task<ServiceContext> GetAsync(TenantId tenantId, EnvironmentId environmentId, ServiceId serviceId, CancellationToken cancellationToken) =>
        grains.GetGrain<IServiceGrain>(Key(tenantId, environmentId, serviceId)).Get();

    public async Task ObserveEdgeAsync(TenantId tenantId, EnvironmentId environmentId, ServiceId caller, ServiceId callee, CancellationToken cancellationToken)
    {
        await grains.GetGrain<IServiceGrain>(Key(tenantId, environmentId, caller)).ObserveDependency(callee);
        await grains.GetGrain<IServiceGrain>(Key(tenantId, environmentId, callee)).ObserveDependent(caller);
    }

    private static string Key(TenantId tenantId, EnvironmentId environmentId, ServiceId serviceId) => $"{tenantId}/{environmentId}/{serviceId}";
}
