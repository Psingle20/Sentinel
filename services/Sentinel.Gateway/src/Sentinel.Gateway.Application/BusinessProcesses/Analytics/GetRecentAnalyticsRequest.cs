using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.Gateway.Application.Analytics;

namespace Sentinel.Gateway.Application.BusinessProcesses.Analytics;

/// <summary>The daily analytics counts of the last days, for operators.</summary>
public sealed record GetRecentAnalyticsRequest(int Days) : IRequest<IReadOnlyList<AnalyticsCount>>;
