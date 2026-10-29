using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.Gateway.Application.Abstractions.Persistence;
using Sentinel.Gateway.Application.Analytics;

namespace Sentinel.Gateway.Application.BusinessTasks.Queries.Analytics;

/// <summary>The counts of the last 1 to 366 days, today included.</summary>
public sealed class GetRecentAnalyticsQueryHandler(IAnalyticsStore store, TimeProvider time) : IQueryHandler<GetRecentAnalyticsQuery, IReadOnlyList<AnalyticsCount>>
{
    public async Task<Result<IReadOnlyList<AnalyticsCount>>> Handle(GetRecentAnalyticsQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var today = DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime);
        return Result.Success(await store.SinceAsync(today.AddDays(-Math.Clamp(query.Days, 1, 366) + 1), cancellationToken));
    }
}
