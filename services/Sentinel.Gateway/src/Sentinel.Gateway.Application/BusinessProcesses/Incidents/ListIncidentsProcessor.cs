using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.Gateway.Application.BusinessTasks.Queries.Incidents;
using Sentinel.Contracts.Incidents;
using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.Gateway.Application.BusinessProcesses.Incidents;

public sealed class ListIncidentsProcessor(IQueryDispatcher queries) : IProcessor<ListIncidentsRequest, IReadOnlyList<IncidentSummary>>
{
    public Task<Result<IReadOnlyList<IncidentSummary>>> Process(ListIncidentsRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return queries.Dispatch(new ListIncidentsQuery(request.Limit, request.Tenants), cancellationToken);
    }
}
