using Sentinel.Contracts.Incidents;
using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.Gateway.Application.Abstractions.Providers;

/// <summary>Runtime's incident read model, which the Gateway serves and never owns (ADR-001).</summary>
public interface IRuntimeIncidents
{
    Task<IReadOnlyList<IncidentSummary>> ListAsync(int limit, CancellationToken cancellationToken);

    Task<IncidentDetail?> GetAsync(IncidentId incidentId, CancellationToken cancellationToken);

    Task<IncidentRunRecord?> RunRecordAsync(IncidentId incidentId, CancellationToken cancellationToken);
}
