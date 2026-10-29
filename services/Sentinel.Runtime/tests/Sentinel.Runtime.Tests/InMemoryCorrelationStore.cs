using System.Collections.Concurrent;
using CleanArchitecture.BuildingBlocks.Integration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Sentinel.Contracts;
using Sentinel.Contracts.Incidents;
using Sentinel.Contracts.Observations;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Contracts.Queries;
using Sentinel.Contracts.Remediation;
using Sentinel.Runtime.Application;
using Sentinel.Runtime.Application.Incidents;
using Sentinel.Runtime.Application.Memory;
using Sentinel.Runtime.Domain.Correlation;
using Sentinel.Runtime.Domain.Incidents;
using Sentinel.Runtime.Application.Abstractions.Runtime;
using Sentinel.Runtime.Application.Abstractions.Persistence;
using Sentinel.Runtime.Application.Abstractions.Providers;
using Sentinel.Runtime.Application.Incidents.InProcess;
using Sentinel.Runtime.Application.Options;

namespace Sentinel.Runtime.Tests;

internal sealed class InMemoryCorrelationStore : ICorrelationStore
{
    private readonly ConcurrentDictionary<string, SignalStreak> _streaks = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, LinkedChange> _changes = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<(ServiceId, ServiceId), DateTimeOffset> _edges = new();

    public Task<SignalStreak?> GetStreakAsync(TenantId tenantId, EnvironmentId environmentId, ServiceId serviceId, string metricName, CancellationToken cancellationToken) =>
        Task.FromResult(_streaks.TryGetValue($"{serviceId}/{metricName}", out SignalStreak? streak) ? streak : null);

    public Task SaveStreakAsync(SignalStreak streak, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(streak);

        _streaks[$"{streak.ServiceId}/{streak.MetricName}"] = streak;
        return Task.CompletedTask;
    }

    public Task<LinkedChange> SaveChangeAsync(TenantId tenantId, EnvironmentId environmentId, LinkedChange change, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(change);

        LinkedChange saved = _changes.AddOrUpdate(change.DeploymentId, change, (_, existing) => existing with
        {
            CompletedAt = change.CompletedAt ?? existing.CompletedAt,
            Failed = existing.Failed || change.Failed,
            ObservationIds = [.. existing.ObservationIds.Concat(change.ObservationIds).Distinct()]
        });
        return Task.FromResult(saved);
    }

    public Task<IReadOnlyList<LinkedChange>> GetChangesAsync(TenantId tenantId, EnvironmentId environmentId, IReadOnlyCollection<ServiceId> services, DateTimeOffset startedFrom, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<LinkedChange>>([.. _changes.Values
            .Where(change => (services.Count == 0 || services.Contains(change.ServiceId)) && change.StartedAt >= startedFrom)
            .OrderBy(change => change.StartedAt)]);

    public Task<LinkedChange?> GetChangeAsync(TenantId tenantId, EnvironmentId environmentId, string deploymentId, CancellationToken cancellationToken) =>
        Task.FromResult(_changes.TryGetValue(deploymentId, out LinkedChange? change) ? change : null);

    public Task SaveEdgeAsync(TenantId tenantId, EnvironmentId environmentId, ServiceId caller, ServiceId callee, DateTimeOffset seenAt, CancellationToken cancellationToken)
    {
        _edges[(caller, callee)] = seenAt;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<(ServiceId Caller, ServiceId Callee)>> GetEdgesAsync(TenantId tenantId, EnvironmentId environmentId, DateTimeOffset seenSince, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<(ServiceId Caller, ServiceId Callee)>>([.. _edges.Keys]);
}
