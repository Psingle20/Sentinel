using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.AgentWorker.Application.BusinessTasks.Queries.Configuration;
using CleanArchitecture.BuildingBlocks.Configuration;

namespace Sentinel.AgentWorker.Application.BusinessProcesses.Configuration;

public sealed class GetModelConfigurationHistoryProcessor(IQueryDispatcher queries) : IProcessor<GetModelConfigurationHistoryRequest, IReadOnlyList<DocumentVersion>>
{
    public Task<Result<IReadOnlyList<DocumentVersion>>> Process(GetModelConfigurationHistoryRequest request, CancellationToken cancellationToken)
    {
        return queries.Dispatch(new GetModelConfigurationHistoryQuery(), cancellationToken);
    }
}
