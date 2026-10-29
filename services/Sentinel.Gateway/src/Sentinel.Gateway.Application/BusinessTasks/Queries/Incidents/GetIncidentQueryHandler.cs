using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.Contracts.Incidents;
using Sentinel.Gateway.Application.Abstractions.Providers;

namespace Sentinel.Gateway.Application.BusinessTasks.Queries.Incidents;

public sealed class GetIncidentQueryHandler(IRuntimeIncidents incidents) : IQueryHandler<GetIncidentQuery, IncidentDetail?>
{
    public async Task<Result<IncidentDetail?>> Handle(GetIncidentQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        return Result.Success(await incidents.GetAsync(query.IncidentId, cancellationToken));
    }
}
