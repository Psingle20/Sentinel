using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.Contracts.Incidents;
using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.Gateway.Application.BusinessTasks.Queries.Postmortems;

public sealed record GetPostmortemQuery(IncidentId IncidentId) : IQuery<Postmortem?>;
