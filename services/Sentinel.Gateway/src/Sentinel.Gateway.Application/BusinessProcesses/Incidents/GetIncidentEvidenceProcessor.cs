using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.Gateway.Application.BusinessTasks.Queries.Incidents;
using Sentinel.Contracts.Incidents;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Gateway.Application.Incidents;

namespace Sentinel.Gateway.Application.BusinessProcesses.Incidents;

public sealed class GetIncidentEvidenceProcessor(IQueryDispatcher queries) : IProcessor<GetIncidentEvidenceRequest, Page<EvidenceView>?>
{
    public Task<Result<Page<EvidenceView>?>> Process(GetIncidentEvidenceRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return queries.Dispatch(new GetIncidentEvidenceQuery(request.IncidentId, request.Offset, request.Limit), cancellationToken);
    }
}
