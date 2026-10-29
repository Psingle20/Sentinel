using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.Contracts.Incidents;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Gateway.Application.Incidents;

namespace Sentinel.Gateway.Application.BusinessProcesses.Incidents;

/// <summary>A page of an incident's timeline, newest first, before a stream version.</summary>
public sealed record GetIncidentTimelineRequest(IncidentId IncidentId, long? Before, int? Limit) : IRequest<Page<TimelineEntry>?>;
