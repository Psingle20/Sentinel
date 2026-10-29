using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.Contracts.Incidents;
using Sentinel.Gateway.Application.Abstractions.Providers;

namespace Sentinel.Gateway.Application.BusinessTasks.Queries.Incidents;

public sealed class GetRunRecordQueryHandler(IRuntimeIncidents incidents) : IQueryHandler<GetRunRecordQuery, IncidentRunRecord?>
{
    public async Task<Result<IncidentRunRecord?>> Handle(GetRunRecordQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        return Result.Success(await incidents.RunRecordAsync(query.IncidentId, cancellationToken));
    }
}
