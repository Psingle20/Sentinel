using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.Runtime.Application.BusinessTasks.Queries.Configuration;
using CleanArchitecture.BuildingBlocks.Configuration;

namespace Sentinel.Runtime.Application.BusinessProcesses.Configuration;

public sealed class GetRuntimeConfigurationHistoryProcessor(IQueryDispatcher queries) : IProcessor<GetRuntimeConfigurationHistoryRequest, IReadOnlyList<DocumentVersion>>
{
    public Task<Result<IReadOnlyList<DocumentVersion>>> Process(GetRuntimeConfigurationHistoryRequest request, CancellationToken cancellationToken)
    {
        return queries.Dispatch(new GetRuntimeConfigurationHistoryQuery(), cancellationToken);
    }
}
