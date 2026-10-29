using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.Gateway.Application.Live;

namespace Sentinel.Gateway.Application.BusinessProcesses.Live;

/// <summary>One incident lifecycle message to record as a live event and route to notifications.</summary>
public sealed record RecordIncidentEventRequest(LiveEventDraft Draft) : IRequest;
