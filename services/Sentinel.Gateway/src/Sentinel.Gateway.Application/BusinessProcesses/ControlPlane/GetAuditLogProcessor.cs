using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.Gateway.Application.BusinessTasks.Queries.ControlPlane;
using Sentinel.Gateway.Application.Abstractions.Persistence;

namespace Sentinel.Gateway.Application.BusinessProcesses.ControlPlane;

public sealed class GetAuditLogProcessor(IQueryDispatcher queries) : IProcessor<GetAuditLogRequest, IReadOnlyList<AuditEntry>>
{
    public Task<Result<IReadOnlyList<AuditEntry>>> Process(GetAuditLogRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return queries.Dispatch(new GetAuditLogQuery(request.Limit), cancellationToken);
    }
}
