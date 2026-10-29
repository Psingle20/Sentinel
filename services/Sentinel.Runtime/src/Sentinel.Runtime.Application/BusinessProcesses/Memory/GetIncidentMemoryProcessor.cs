using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.Runtime.Application.BusinessTasks.Queries.Memory;
using Sentinel.Contracts.Incidents;
using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.Runtime.Application.BusinessProcesses.Memory;

public sealed class GetIncidentMemoryProcessor(IQueryDispatcher queries) : IProcessor<GetIncidentMemoryRequest, IncidentMemoryView?>
{
    public Task<Result<IncidentMemoryView?>> Process(GetIncidentMemoryRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return queries.Dispatch(new GetIncidentMemoryQuery(request.IncidentId), cancellationToken);
    }
}
