using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.Runtime.Application.BusinessTasks.Queries.Incidents;
using Sentinel.Contracts.Incidents;
using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.Runtime.Application.BusinessProcesses.Incidents;

public sealed class GetIncidentProcessor(IQueryDispatcher queries) : IProcessor<GetIncidentRequest, IncidentDetail>
{
    public Task<Result<IncidentDetail>> Process(GetIncidentRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return queries.Dispatch(new GetIncidentQuery(request.IncidentId), cancellationToken);
    }
}
