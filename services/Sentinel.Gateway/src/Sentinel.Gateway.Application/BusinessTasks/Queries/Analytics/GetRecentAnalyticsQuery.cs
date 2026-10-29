using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.Gateway.Application.Analytics;

namespace Sentinel.Gateway.Application.BusinessTasks.Queries.Analytics;

public sealed record GetRecentAnalyticsQuery(int Days) : IQuery<IReadOnlyList<AnalyticsCount>>;
