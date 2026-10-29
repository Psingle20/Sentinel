using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.Contracts.Incidents;
using Sentinel.Runtime.Application.Memory;

namespace Sentinel.Runtime.Application.BusinessTasks.Queries.Incidents;

/// <summary>Incidents similar to a remembered or open one, within its tenant and the memory's privacy and retention rules.</summary>
public sealed class GetSimilarIncidentsQueryHandler(IncidentSimilarity similarity) : IQueryHandler<GetSimilarIncidentsQuery, IReadOnlyList<SimilarIncident>?>
{
    public async Task<Result<IReadOnlyList<SimilarIncident>?>> Handle(GetSimilarIncidentsQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        return Result.Success(await similarity.SimilarToAsync(query.IncidentId, cancellationToken));
    }
}
