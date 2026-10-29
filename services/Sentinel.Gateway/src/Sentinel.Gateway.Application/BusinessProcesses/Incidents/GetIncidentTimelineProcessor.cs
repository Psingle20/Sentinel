using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.Gateway.Application.BusinessTasks.Queries.Incidents;
using Sentinel.Contracts.Incidents;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Gateway.Application.Incidents;

namespace Sentinel.Gateway.Application.BusinessProcesses.Incidents;

public sealed class GetIncidentTimelineProcessor(IQueryDispatcher queries) : IProcessor<GetIncidentTimelineRequest, Page<TimelineEntry>?>
{
    public Task<Result<Page<TimelineEntry>?>> Process(GetIncidentTimelineRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return queries.Dispatch(new GetIncidentTimelineQuery(request.IncidentId, request.Before, request.Limit), cancellationToken);
    }
}
