using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.Contracts.Incidents;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Gateway.Application.Incidents;

namespace Sentinel.Gateway.Application.BusinessTasks.Queries.Incidents;

public sealed record GetIncidentTimelineQuery(IncidentId IncidentId, long? Before, int? Limit) : IQuery<Page<TimelineEntry>?>;
