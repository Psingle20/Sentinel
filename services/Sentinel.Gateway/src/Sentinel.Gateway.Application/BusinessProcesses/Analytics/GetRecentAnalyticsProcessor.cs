using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.Gateway.Application.BusinessTasks.Queries.Analytics;
using Sentinel.Gateway.Application.Analytics;

namespace Sentinel.Gateway.Application.BusinessProcesses.Analytics;

public sealed class GetRecentAnalyticsProcessor(IQueryDispatcher queries) : IProcessor<GetRecentAnalyticsRequest, IReadOnlyList<AnalyticsCount>>
{
    public Task<Result<IReadOnlyList<AnalyticsCount>>> Process(GetRecentAnalyticsRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return queries.Dispatch(new GetRecentAnalyticsQuery(request.Days), cancellationToken);
    }
}
