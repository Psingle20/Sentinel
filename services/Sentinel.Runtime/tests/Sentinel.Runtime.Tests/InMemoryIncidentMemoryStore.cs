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

/// <summary>Incident memory with the production store's semantics: tenant-scoped retrieval, retention, legal hold and final deletion.</summary>
internal sealed class InMemoryIncidentMemoryStore : IIncidentMemoryStore
{
    private readonly ConcurrentDictionary<IncidentId, IncidentMemoryView> _records = new();

    public IReadOnlyCollection<IncidentMemoryView> Records => [.. _records.Values];

    public Task SaveAsync(IncidentMemoryView memory, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(memory);

        _records.AddOrUpdate(memory.IncidentId, memory, (_, existing) => existing.Retention.DeletedAt is null ? memory : existing);
        return Task.CompletedTask;
    }

    public Task<IncidentMemoryView?> GetAsync(IncidentId incidentId, CancellationToken cancellationToken) =>
        Task.FromResult(_records.TryGetValue(incidentId, out IncidentMemoryView? memory) ? memory : null);

    public Task<IReadOnlyList<IncidentMemoryView>> CandidatesAsync(TenantId tenantId, EnvironmentId? environmentId, IReadOnlyCollection<string> features, DateTimeOffset now, int limit, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<IncidentMemoryView>>([.. _records.Values
            .Where(memory => memory.TenantId == tenantId
                && (environmentId is null || memory.EnvironmentId == environmentId)
                && memory.Retention.DeletedAt is null
                && (memory.Retention.RetainUntil > now || memory.Retention.LegalHold)
                && memory.Features.Any(features.Contains))
            .OrderByDescending(memory => memory.RecordedAt)
            .Take(limit)]);

    public Task<IReadOnlyList<IncidentMemoryView>> ExpiredAsync(DateTimeOffset now, int limit, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<IncidentMemoryView>>([.. _records.Values
            .Where(memory => memory.Retention.DeletedAt is null && !memory.Retention.LegalHold && memory.Retention.RetainUntil <= now)
            .OrderBy(memory => memory.Retention.RetainUntil)
            .Take(limit)]);

    public Task DeleteAsync(IncidentMemoryView tombstone, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tombstone);

        _records[tombstone.IncidentId] = tombstone;
        return Task.CompletedTask;
    }
}
