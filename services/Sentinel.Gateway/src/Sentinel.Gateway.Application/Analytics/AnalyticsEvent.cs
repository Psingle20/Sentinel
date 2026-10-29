using CleanArchitecture.BuildingBlocks;

namespace Sentinel.Gateway.Application.Analytics;

/// <summary>One accepted event as the web app reports it.</summary>
public sealed record AnalyticsEvent(string Name, IReadOnlyDictionary<string, string>? Properties);
