using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.Contracts.Incidents;
using Sentinel.Runtime.Application.Abstractions.Persistence;
using Sentinel.Runtime.Application.Incidents;
using Sentinel.Runtime.Domain.Incidents;

namespace Sentinel.Runtime.Application.BusinessTasks.Queries.Incidents;

/// <summary>One incident from the read model.</summary>
public sealed class GetIncidentQueryHandler(IIncidentStore incidents) : IQueryHandler<GetIncidentQuery, IncidentDetail>
{
    public async Task<Result<IncidentDetail>> Handle(GetIncidentQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        return await incidents.GetDetailAsync(query.IncidentId, cancellationToken) is { } detail
            ? Result.Success(detail)
            : Result.Failure<IncidentDetail>(IncidentErrors.NotFound);
    }
}
