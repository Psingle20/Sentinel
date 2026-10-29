using CleanArchitecture.BuildingBlocks;
using Sentinel.Contracts.Incidents;
using Sentinel.Contracts.Observations;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Contracts.Queries;
using Sentinel.Contracts.Remediation;
using Sentinel.Runtime.Application.Incidents;
using Sentinel.Runtime.Domain.Correlation;
using Sentinel.Runtime.Domain.Incidents;

namespace Sentinel.Runtime.Application.Abstractions.Persistence;

/// <summary>
/// The incident event store and its read model. Appends are optimistic on the expected version;
/// the read model is a projection, rebuildable from the stream (ADR-004).
/// </summary>
public interface IIncidentStore
{
    Task<IReadOnlyList<StoredEvent>> ReadStreamAsync(IncidentId incidentId, CancellationToken cancellationToken);

    Task AppendAsync(Incident incident, long expectedVersion, IReadOnlyList<StoredEvent> events, CancellationToken cancellationToken);

    Task SaveProjectionAsync(IncidentDetail detail, bool isOpen, CancellationToken cancellationToken);

    Task<IncidentId?> FindOpenAsync(TenantId tenantId, EnvironmentId environmentId, ServiceId serviceId, CancellationToken cancellationToken);

    /// <summary>Open incidents and those resolved since <paramref name="resolvedSince"/>, for correlation.</summary>
    Task<IReadOnlyList<IncidentContext>> CorrelationCandidatesAsync(TenantId tenantId, EnvironmentId environmentId, DateTimeOffset resolvedSince, CancellationToken cancellationToken);

    /// <summary>The stream version each projection was built from, to report projection lag.</summary>
    Task<long?> ProjectionVersionAsync(IncidentId incidentId, CancellationToken cancellationToken);

    Task<IReadOnlyList<IncidentSummary>> ListAsync(int limit, CancellationToken cancellationToken);

    Task<IncidentDetail?> GetDetailAsync(IncidentId incidentId, CancellationToken cancellationToken);

    Task<IReadOnlyList<IncidentId>> AllIncidentIdsAsync(CancellationToken cancellationToken);
}
