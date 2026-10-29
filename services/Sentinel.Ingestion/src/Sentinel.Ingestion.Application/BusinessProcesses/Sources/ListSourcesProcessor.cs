using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.Ingestion.Application.BusinessTasks.Queries.Sources;

namespace Sentinel.Ingestion.Application.BusinessProcesses.Sources;

public sealed class ListSourcesProcessor(IQueryDispatcher queries) : IProcessor<ListSourcesRequest, IReadOnlyList<SourceSummary>>
{
    public Task<Result<IReadOnlyList<SourceSummary>>> Process(ListSourcesRequest request, CancellationToken cancellationToken)
    {
        return queries.Dispatch(new ListSourcesQuery(), cancellationToken);
    }
}
