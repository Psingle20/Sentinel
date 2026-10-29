using CleanArchitecture.BuildingBlocks;

namespace Sentinel.Gateway.Application.Analytics;

/// <summary>A day's count for one event and one combination of property values.</summary>
public sealed record AnalyticsCount(DateOnly Day, string Event, string Dimensions, long Count);
