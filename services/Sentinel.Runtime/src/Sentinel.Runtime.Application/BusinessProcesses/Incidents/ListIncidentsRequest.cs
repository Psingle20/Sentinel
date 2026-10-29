using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.Contracts.Incidents;

namespace Sentinel.Runtime.Application.BusinessProcesses.Incidents;

/// <summary>The Gateway lists recent incidents from the read model.</summary>
public sealed record ListIncidentsRequest(int Limit) : IRequest<IReadOnlyList<IncidentSummary>>;
