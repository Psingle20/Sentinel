using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.Gateway.Application.Abstractions.Persistence;

namespace Sentinel.Gateway.Application.BusinessTasks.Queries.ControlPlane;

/// <summary>The most recent 1 to 500 audit entries.</summary>
public sealed class GetAuditLogQueryHandler(IAuditLog audit) : IQueryHandler<GetAuditLogQuery, IReadOnlyList<AuditEntry>>
{
    public async Task<Result<IReadOnlyList<AuditEntry>>> Handle(GetAuditLogQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        return Result.Success(await audit.RecentAsync(Math.Clamp(query.Limit, 1, 500), cancellationToken));
    }
}
