using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.Contracts.Incidents;
using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.Runtime.Application.BusinessTasks.Queries.Incidents;

public sealed record GetPostmortemQuery(IncidentId IncidentId) : IQuery<Postmortem?>;
