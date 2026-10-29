using CleanArchitecture.BuildingBlocks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sentinel.Contracts.Observations;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Runtime.Application;
using Sentinel.Runtime.Application.Incidents;
using Sentinel.Runtime.Application.Abstractions.Runtime;
using Sentinel.Runtime.Application.Abstractions.Persistence;
using Sentinel.Runtime.Application.Detection;
using Sentinel.Runtime.Application.Options;

namespace Sentinel.Runtime.Infrastructure.Grains;

/// <summary>
/// Caches a service's catalog facts and its observed dependency edges. Never authoritative: on
/// activation it is rebuilt from configuration and Runtime's correlation store, and only edges it
/// has not seen recently are written back.
/// </summary>
internal sealed class ServiceGrain(IServiceScopeFactory scopes, ServiceTopologyCache topologies, TimeProvider time, IOptions<RuntimeOptions> options) : Grain, IServiceGrain
{
    private static readonly TimeSpan PersistEvery = TimeSpan.FromMinutes(1);

    private readonly HashSet<ServiceId> _dependencies = [];
    private readonly HashSet<ServiceId> _dependents = [];
    private readonly Dictionary<ServiceId, DateTimeOffset> _persisted = [];
    private TenantId _tenant;
    private EnvironmentId _environment;
    private ServiceId _service;

    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        string[] key = this.GetPrimaryKeyString().Split('/');
        _tenant = TenantId.From(key[0]);
        _environment = EnvironmentId.From(key[1]);
        _service = ServiceId.From(key[2]);

        RuntimeOptions settings = options.Value;
        foreach ((string caller, string[] callees) in settings.Dependencies)
        {
            foreach (string callee in callees)
            {
                if (caller == _service.Value)
                {
                    _dependencies.Add(ServiceId.From(callee));
                }
                else if (callee == _service.Value)
                {
                    _dependents.Add(ServiceId.From(caller));
                }
            }
        }

        await using AsyncServiceScope scope = scopes.CreateAsyncScope();
        foreach ((ServiceId caller, ServiceId callee) in await scope.ServiceProvider.GetRequiredService<ICorrelationStore>()
            .GetEdgesAsync(_tenant, _environment, time.GetUtcNow() - settings.Correlation.EdgeRetention, cancellationToken))
        {
            if (caller == _service)
            {
                _dependencies.Add(callee);
            }
            else if (callee == _service)
            {
                _dependents.Add(caller);
            }
        }

        await base.OnActivateAsync(cancellationToken);
    }

    public Task<ServiceContext> Get() =>
        Task.FromResult(new ServiceContext(_service, options.Value.Owners.TryGetValue(_service.Value, out string? owner) ? owner : null, [.. _dependencies], [.. _dependents]));

    public async Task ObserveDependency(ServiceId callee)
    {
        DateTimeOffset now = time.GetUtcNow();
        bool known = _dependencies.Contains(callee);
        if (known && _persisted.TryGetValue(callee, out DateTimeOffset persisted) && now - persisted < PersistEvery)
        {
            return;
        }

        await using (AsyncServiceScope scope = scopes.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<ICorrelationStore>().SaveEdgeAsync(_tenant, _environment, _service, callee, now, CancellationToken.None);
            await scope.ServiceProvider.GetRequiredService<CleanArchitecture.BuildingBlocks.Integration.IUnitOfWork>().CommitAsync(CancellationToken.None);
        }

        _persisted[callee] = now;
        if (_dependencies.Add(callee))
        {
            topologies.Invalidate(_tenant, _environment);
        }
    }

    public Task ObserveDependent(ServiceId caller)
    {
        _dependents.Add(caller);
        return Task.CompletedTask;
    }
}
