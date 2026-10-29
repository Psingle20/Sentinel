using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.Gateway.Application.Analytics;

namespace Sentinel.Gateway.Application.BusinessTasks.Commands.Analytics;

public sealed record RecordAnalyticsEventCommand(AnalyticsEvent Event) : ICommand;
