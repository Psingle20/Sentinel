using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.Executor.Application.BusinessTasks.Queries.Executions;
using Sentinel.Executor.Domain.Executions;

namespace Sentinel.Executor.Application.BusinessProcesses.Executions;

public sealed class ListExecutionsProcessor(IQueryDispatcher queries) : IProcessor<ListExecutionsRequest, IReadOnlyList<ExecutionSnapshot>>
{
    public Task<Result<IReadOnlyList<ExecutionSnapshot>>> Process(ListExecutionsRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return queries.Dispatch(new ListExecutionsQuery(request.Limit), cancellationToken);
    }
}
