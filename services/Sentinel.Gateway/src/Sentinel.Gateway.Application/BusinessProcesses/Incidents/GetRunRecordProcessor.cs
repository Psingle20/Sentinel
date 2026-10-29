using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.Gateway.Application.BusinessTasks.Queries.Incidents;
using Sentinel.Contracts.Incidents;
using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.Gateway.Application.BusinessProcesses.Incidents;

public sealed class GetRunRecordProcessor(IQueryDispatcher queries) : IProcessor<GetRunRecordRequest, IncidentRunRecord?>
{
    public Task<Result<IncidentRunRecord?>> Process(GetRunRecordRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return queries.Dispatch(new GetRunRecordQuery(request.IncidentId), cancellationToken);
    }
}
