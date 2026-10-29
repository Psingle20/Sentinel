using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.Ingestion.Application.BusinessTasks.Queries.Configuration;
using CleanArchitecture.BuildingBlocks.Configuration;

namespace Sentinel.Ingestion.Application.BusinessProcesses.Configuration;

public sealed class GetSourceConfigurationHistoryProcessor(IQueryDispatcher queries) : IProcessor<GetSourceConfigurationHistoryRequest, IReadOnlyList<DocumentVersion>>
{
    public Task<Result<IReadOnlyList<DocumentVersion>>> Process(GetSourceConfigurationHistoryRequest request, CancellationToken cancellationToken)
    {
        return queries.Dispatch(new GetSourceConfigurationHistoryQuery(), cancellationToken);
    }
}
