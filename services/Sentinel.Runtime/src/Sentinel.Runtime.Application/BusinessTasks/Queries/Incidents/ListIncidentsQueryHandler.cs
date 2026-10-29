using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.Contracts.Incidents;
using Sentinel.Runtime.Application.Abstractions.Persistence;

namespace Sentinel.Runtime.Application.BusinessTasks.Queries.Incidents;

/// <summary>Recent incidents from the read model, at most 200.</summary>
public sealed class ListIncidentsQueryHandler(IIncidentStore incidents) : IQueryHandler<ListIncidentsQuery, IReadOnlyList<IncidentSummary>>
{
    public async Task<Result<IReadOnlyList<IncidentSummary>>> Handle(ListIncidentsQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        return Result.Success(await incidents.ListAsync(Math.Clamp(query.Limit, 1, 200), cancellationToken));
    }
}
