using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.Contracts.Incidents;
using Sentinel.Gateway.Application.Abstractions.Providers;

namespace Sentinel.Gateway.Application.BusinessTasks.Queries.Incidents;

public sealed class GetSimilarIncidentsQueryHandler(IRuntimeHistory history) : IQueryHandler<GetSimilarIncidentsQuery, IReadOnlyList<SimilarIncident>?>
{
    public async Task<Result<IReadOnlyList<SimilarIncident>?>> Handle(GetSimilarIncidentsQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        return Result.Success(await history.SimilarAsync(query.IncidentId, cancellationToken));
    }
}
