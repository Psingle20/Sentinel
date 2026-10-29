using System.Text.Json;
using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Integration;
using Sentinel.Contracts.Incidents;
using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.Gateway.Application.Abstractions.Persistence;

/// <summary>Api's own store for the human-written part of postmortems.</summary>
public interface INarrativeStore
{
    Task<PostmortemNarrative?> GetAsync(IncidentId incidentId, CancellationToken cancellationToken);

    /// <summary>Saves <paramref name="narrative"/> only if the stored version is still <paramref name="expectedVersion"/>; false when another edit won.</summary>
    Task<bool> SaveAsync(IncidentId incidentId, PostmortemNarrative narrative, int expectedVersion, CancellationToken cancellationToken);
}
