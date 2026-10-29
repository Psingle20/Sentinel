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
/// Incident memory (Phase 07): one record per closed incident and the feature index similar-incident
/// retrieval reads. The index is derived; deleting a record removes it from the index too.
/// </summary>
public interface IIncidentMemoryStore
{
    /// <summary>Inserts or replaces the record and its features, unless the record was deleted.</summary>
    Task SaveAsync(IncidentMemoryView memory, CancellationToken cancellationToken);

    Task<IncidentMemoryView?> GetAsync(IncidentId incidentId, CancellationToken cancellationToken);

    /// <summary>
    /// Records of the tenant sharing at least one of <paramref name="features"/>, in
    /// <paramref name="environmentId"/> unless it is null, retained at <paramref name="now"/> or on
    /// legal hold, and not deleted. Never returns another tenant's memory.
    /// </summary>
    Task<IReadOnlyList<IncidentMemoryView>> CandidatesAsync(TenantId tenantId, EnvironmentId? environmentId, IReadOnlyCollection<string> features, DateTimeOffset now, int limit, CancellationToken cancellationToken);

    /// <summary>Records past their retention, not on legal hold and not yet deleted.</summary>
    Task<IReadOnlyList<IncidentMemoryView>> ExpiredAsync(DateTimeOffset now, int limit, CancellationToken cancellationToken);

    /// <summary>Replaces a record with its tombstone and removes it from every derived index.</summary>
    Task DeleteAsync(IncidentMemoryView tombstone, CancellationToken cancellationToken);
}
