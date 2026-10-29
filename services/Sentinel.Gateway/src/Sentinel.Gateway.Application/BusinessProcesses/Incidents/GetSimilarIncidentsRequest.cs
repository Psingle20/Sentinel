using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.Contracts.Incidents;
using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.Gateway.Application.BusinessProcesses.Incidents;

/// <summary>Remembered incidents like this one (Phase 07).</summary>
public sealed record GetSimilarIncidentsRequest(IncidentId IncidentId) : IRequest<IReadOnlyList<SimilarIncident>?>;
