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

/// <summary>An event store with the production store's optimistic append and a rebuildable projection.</summary>
internal sealed class InMemoryIncidentStore : IIncidentStore
{
    private readonly object _gate = new();
    private readonly Dictionary<IncidentId, List<StoredEvent>> _streams = [];
    private readonly Dictionary<IncidentId, (IncidentDetail Detail, bool IsOpen)> _projections = [];

    public int AppendCount { get; private set; }

    public Task<IReadOnlyList<StoredEvent>> ReadStreamAsync(IncidentId incidentId, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return Task.FromResult<IReadOnlyList<StoredEvent>>(_streams.TryGetValue(incidentId, out List<StoredEvent>? stream) ? [.. stream] : []);
        }
    }

    public Task AppendAsync(Incident incident, long expectedVersion, IReadOnlyList<StoredEvent> events, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(incident);
        ArgumentNullException.ThrowIfNull(events);

        lock (_gate)
        {
            if (!_streams.TryGetValue(incident.Id, out List<StoredEvent>? stream))
            {
                stream = [];
                _streams[incident.Id] = stream;
            }

            if (stream.Count != expectedVersion)
            {
                throw new IncidentConcurrencyException(incident.Id, expectedVersion);
            }

            stream.AddRange(events);
            AppendCount++;
        }

        return Task.CompletedTask;
    }

    public Task SaveProjectionAsync(IncidentDetail detail, bool isOpen, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(detail);

        lock (_gate)
        {
            _projections[detail.Summary.IncidentId] = (detail, isOpen);
        }

        return Task.CompletedTask;
    }

    /// <summary>Deletes every projection, as an operator might before a rebuild.</summary>
    public void DeleteProjections()
    {
        lock (_gate)
        {
            _projections.Clear();
        }
    }

    public Task<IncidentId?> FindOpenAsync(TenantId tenantId, EnvironmentId environmentId, ServiceId serviceId, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return Task.FromResult<IncidentId?>(_projections.Values
                .Where(entry => entry.IsOpen && entry.Detail.Summary.AffectedServiceId == serviceId)
                .Select(entry => (IncidentId?)entry.Detail.Summary.IncidentId)
                .FirstOrDefault());
        }
    }

    public Task<IReadOnlyList<IncidentContext>> CorrelationCandidatesAsync(TenantId tenantId, EnvironmentId environmentId, DateTimeOffset resolvedSince, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return Task.FromResult<IReadOnlyList<IncidentContext>>([.. _projections.Values
                .Where(entry => entry.IsOpen || entry.Detail.Summary.UpdatedAt >= resolvedSince)
                .Select(entry => new IncidentContext(
                    entry.Detail.Summary.IncidentId,
                    entry.Detail.Summary.AffectedServiceId,
                    entry.Detail.AffectedServices,
                    entry.Detail.Summary.SymptomOnset ?? entry.Detail.Summary.OpenedAt,
                    entry.IsOpen,
                    entry.IsOpen ? null : entry.Detail.Summary.UpdatedAt))]);
        }
    }

    public Task<long?> ProjectionVersionAsync(IncidentId incidentId, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return Task.FromResult<long?>(_projections.TryGetValue(incidentId, out (IncidentDetail Detail, bool IsOpen) entry) ? entry.Detail.Summary.StreamVersion : null);
        }
    }

    public Task<IReadOnlyList<IncidentSummary>> ListAsync(int limit, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return Task.FromResult<IReadOnlyList<IncidentSummary>>([.. _projections.Values.Select(entry => entry.Detail.Summary).OrderByDescending(summary => summary.OpenedAt).Take(limit)]);
        }
    }

    public Task<IncidentDetail?> GetDetailAsync(IncidentId incidentId, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return Task.FromResult(_projections.TryGetValue(incidentId, out (IncidentDetail Detail, bool IsOpen) entry) ? entry.Detail : null);
        }
    }

    public Task<IReadOnlyList<IncidentId>> AllIncidentIdsAsync(CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return Task.FromResult<IReadOnlyList<IncidentId>>([.. _streams.Keys]);
        }
    }
}
