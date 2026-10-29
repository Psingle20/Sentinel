using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.Ingestion.Application.BusinessTasks.Queries.Knowledge;
using Sentinel.Contracts.Queries;

namespace Sentinel.Ingestion.Application.BusinessProcesses.Knowledge;

public sealed class SearchKnowledgeProcessor(IQueryDispatcher queries) : IProcessor<SearchKnowledgeRequest, KnowledgeResult>
{
    public Task<Result<KnowledgeResult>> Process(SearchKnowledgeRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return queries.Dispatch(new SearchKnowledgeQuery(request.Query), cancellationToken);
    }
}
