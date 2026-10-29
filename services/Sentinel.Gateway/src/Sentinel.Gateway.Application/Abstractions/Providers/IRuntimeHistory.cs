using System.Text.Json;
using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Integration;
using Sentinel.Contracts.Incidents;
using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.Gateway.Application.Abstractions.Providers;

/// <summary>Runtime's incident history (Phase 07): postmortems, similar incidents and domain replay. Api serves them and owns none of them.</summary>
public interface IRuntimeHistory
{
    Task<Postmortem?> PostmortemAsync(IncidentId incidentId, CancellationToken cancellationToken);

    Task<IReadOnlyList<SimilarIncident>?> SimilarAsync(IncidentId incidentId, CancellationToken cancellationToken);

    Task<DomainReplayResult?> ReplayAsync(IncidentId incidentId, CancellationToken cancellationToken);
}
