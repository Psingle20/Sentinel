using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.Gateway.Application.BusinessTasks.Queries.Incidents;
using Sentinel.Contracts.Incidents;
using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.Gateway.Application.BusinessProcesses.Incidents;

public sealed class GetSimilarIncidentsProcessor(IQueryDispatcher queries) : IProcessor<GetSimilarIncidentsRequest, IReadOnlyList<SimilarIncident>?>
{
    public Task<Result<IReadOnlyList<SimilarIncident>?>> Process(GetSimilarIncidentsRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return queries.Dispatch(new GetSimilarIncidentsQuery(request.IncidentId), cancellationToken);
    }
}
