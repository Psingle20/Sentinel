using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.Runtime.Application.BusinessTasks.Queries.Incidents;
using Sentinel.Contracts.Incidents;

namespace Sentinel.Runtime.Application.BusinessProcesses.Incidents;

public sealed class ListIncidentsProcessor(IQueryDispatcher queries) : IProcessor<ListIncidentsRequest, IReadOnlyList<IncidentSummary>>
{
    public Task<Result<IReadOnlyList<IncidentSummary>>> Process(ListIncidentsRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return queries.Dispatch(new ListIncidentsQuery(request.Limit), cancellationToken);
    }
}
