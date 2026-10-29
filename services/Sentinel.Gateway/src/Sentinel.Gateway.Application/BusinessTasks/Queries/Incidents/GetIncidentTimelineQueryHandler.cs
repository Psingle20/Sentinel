using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.Contracts.Incidents;
using Sentinel.Gateway.Application.Abstractions.Providers;
using Sentinel.Gateway.Application.Incidents;

namespace Sentinel.Gateway.Application.BusinessTasks.Queries.Incidents;

public sealed class GetIncidentTimelineQueryHandler(IRuntimeIncidents incidents) : IQueryHandler<GetIncidentTimelineQuery, Page<TimelineEntry>?>
{
    public async Task<Result<Page<TimelineEntry>?>> Handle(GetIncidentTimelineQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        IncidentDetail? detail = await incidents.GetAsync(query.IncidentId, cancellationToken);
        return Result.Success(detail is null ? null : Pages.Timeline(detail.Timeline ?? [], query.Before, query.Limit));
    }
}
