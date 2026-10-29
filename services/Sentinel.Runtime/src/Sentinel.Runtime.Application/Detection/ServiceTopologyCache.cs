using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Integration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sentinel.Contracts.Observations;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Runtime.Application.Incidents;
using Sentinel.Runtime.Domain.Correlation;
using Sentinel.Runtime.Domain.Incidents;
using Sentinel.Runtime.Application.Abstractions.Runtime;
using Sentinel.Runtime.Application.Abstractions.Persistence;
using Sentinel.Runtime.Application.Options;

namespace Sentinel.Runtime.Application.Detection;

/// <summary>
/// The partition's dependency topology: the service catalog's edges and edges observed in traces,
/// rebuilt at most every <see cref="RefreshInterval"/>. A cache of facts held elsewhere.
/// </summary>
public sealed class ServiceTopologyCache(IServiceScopeFactory scopes, TimeProvider time, IOptions<RuntimeOptions> options) : IDisposable
{
    public static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(30);

    private readonly ConcurrentDictionary<string, (DateTimeOffset BuiltAt, ServiceTopology Topology)> _cache = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<ServiceTopology> GetAsync(TenantId tenantId, EnvironmentId environmentId, CancellationToken cancellationToken)
    {
        string key = Key(tenantId, environmentId);
        DateTimeOffset now = time.GetUtcNow();
        if (_cache.TryGetValue(key, out (DateTimeOffset BuiltAt, ServiceTopology Topology) cached) && now - cached.BuiltAt < RefreshInterval)
        {
            return cached.Topology;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            RuntimeOptions settings = options.Value;
            var edges = settings.Dependencies
                .SelectMany(entry => entry.Value.Select(callee => (Caller: ServiceId.From(entry.Key), Callee: ServiceId.From(callee))))
                .ToList();
            await using (AsyncServiceScope scope = scopes.CreateAsyncScope())
            {
                edges.AddRange(await scope.ServiceProvider.GetRequiredService<ICorrelationStore>().GetEdgesAsync(tenantId, environmentId, now - settings.Correlation.EdgeRetention, cancellationToken));
            }

            var topology = new ServiceTopology(edges);
            _cache[key] = (now, topology);
            return topology;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Forces the next read to rebuild, after a new edge is observed.</summary>
    public void Invalidate(TenantId tenantId, EnvironmentId environmentId) => _cache.TryRemove(Key(tenantId, environmentId), out _);

    public void Dispose() => _gate.Dispose();

    private static string Key(TenantId tenantId, EnvironmentId environmentId) => $"{tenantId}/{environmentId}";
}
