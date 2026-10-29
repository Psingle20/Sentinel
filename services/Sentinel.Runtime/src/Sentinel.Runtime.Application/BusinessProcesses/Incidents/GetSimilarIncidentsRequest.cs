using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.Contracts.Incidents;
using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.Runtime.Application.BusinessProcesses.Incidents;

/// <summary>Remembered incidents similar to one (Phase 07); none for an incident that is neither remembered nor open.</summary>
public sealed record GetSimilarIncidentsRequest(IncidentId IncidentId) : IRequest<IReadOnlyList<SimilarIncident>?>;
