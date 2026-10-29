using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.Runtime.Application.BusinessTasks.Queries.Incidents;
using Sentinel.Contracts.Incidents;
using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.Runtime.Application.BusinessProcesses.Incidents;

public sealed class ReplayDomainProcessor(IQueryDispatcher queries) : IProcessor<ReplayDomainRequest, DomainReplayResult?>
{
    public Task<Result<DomainReplayResult?>> Process(ReplayDomainRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return queries.Dispatch(new ReplayDomainQuery(request.IncidentId), cancellationToken);
    }
}
