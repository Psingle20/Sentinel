using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.Gateway.Application.Analytics;

namespace Sentinel.Gateway.Application.BusinessProcesses.Analytics;

/// <summary>One product analytics event from the web app, counted only if the catalog approves it.</summary>
public sealed record RecordAnalyticsEventRequest(AnalyticsEvent Event) : IRequest;
