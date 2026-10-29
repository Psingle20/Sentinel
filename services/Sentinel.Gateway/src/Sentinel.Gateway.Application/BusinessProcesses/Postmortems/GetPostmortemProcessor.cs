using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.Gateway.Application.BusinessTasks.Queries.Postmortems;
using Sentinel.Contracts.Incidents;
using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.Gateway.Application.BusinessProcesses.Postmortems;

public sealed class GetPostmortemProcessor(IQueryDispatcher queries) : IProcessor<GetPostmortemRequest, Postmortem?>
{
    public Task<Result<Postmortem?>> Process(GetPostmortemRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return queries.Dispatch(new GetPostmortemQuery(request.IncidentId), cancellationToken);
    }
}
