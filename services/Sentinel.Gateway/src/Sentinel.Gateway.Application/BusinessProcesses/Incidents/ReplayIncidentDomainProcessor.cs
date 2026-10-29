using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.Gateway.Application.BusinessTasks.Queries.Incidents;
using Sentinel.Contracts.Incidents;
using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.Gateway.Application.BusinessProcesses.Incidents;

public sealed class ReplayIncidentDomainProcessor(IQueryDispatcher queries) : IProcessor<ReplayIncidentDomainRequest, DomainReplayResult?>
{
    public Task<Result<DomainReplayResult?>> Process(ReplayIncidentDomainRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return queries.Dispatch(new ReplayIncidentDomainQuery(request.IncidentId), cancellationToken);
    }
}
