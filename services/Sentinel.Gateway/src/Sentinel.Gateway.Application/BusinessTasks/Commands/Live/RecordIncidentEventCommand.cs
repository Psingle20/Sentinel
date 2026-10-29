using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.Gateway.Application.Live;

namespace Sentinel.Gateway.Application.BusinessTasks.Commands.Live;

public sealed record RecordIncidentEventCommand(LiveEventDraft Draft) : ICommand;
