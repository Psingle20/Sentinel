using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.Executor.Application.BusinessTasks.Queries.Execution;

namespace Sentinel.Executor.Application.BusinessProcesses.Execution;

public sealed class ListUnfinishedExecutionsProcessor(IQueryDispatcher queries) : IProcessor<ListUnfinishedExecutionsRequest, IReadOnlyList<string>>
{
    public Task<Result<IReadOnlyList<string>>> Process(ListUnfinishedExecutionsRequest request, CancellationToken cancellationToken)
    {
        return queries.Dispatch(new ListUnfinishedExecutionsQuery(), cancellationToken);
    }
}
