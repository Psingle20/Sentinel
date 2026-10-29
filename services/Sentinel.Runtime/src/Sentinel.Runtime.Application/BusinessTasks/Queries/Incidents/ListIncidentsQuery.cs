using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.Contracts.Incidents;

namespace Sentinel.Runtime.Application.BusinessTasks.Queries.Incidents;

public sealed record ListIncidentsQuery(int Limit) : IQuery<IReadOnlyList<IncidentSummary>>;
