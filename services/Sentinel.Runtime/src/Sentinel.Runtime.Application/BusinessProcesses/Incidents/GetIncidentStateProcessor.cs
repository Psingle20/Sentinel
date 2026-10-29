using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.Runtime.Application.BusinessTasks.Queries.Incidents;
using Sentinel.Contracts.Incidents;
using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.Runtime.Application.BusinessProcesses.Incidents;

public sealed class GetIncidentStateProcessor(IQueryDispatcher queries) : IProcessor<GetIncidentStateRequest, IncidentStateView>
{
    public Task<Result<IncidentStateView>> Process(GetIncidentStateRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return queries.Dispatch(new GetIncidentStateQuery(request.IncidentId), cancellationToken);
    }
}
