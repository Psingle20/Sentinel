using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.Ingestion.Application.BusinessTasks.Queries.Observations;
using Sentinel.Contracts.Queries;

namespace Sentinel.Ingestion.Application.BusinessProcesses.Observations;

public sealed class QueryObservationsProcessor(IQueryDispatcher queries) : IProcessor<QueryObservationsRequest, ObservationQueryResult>
{
    public Task<Result<ObservationQueryResult>> Process(QueryObservationsRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return queries.Dispatch(new QueryObservationsQuery(request.Query), cancellationToken);
    }
}
