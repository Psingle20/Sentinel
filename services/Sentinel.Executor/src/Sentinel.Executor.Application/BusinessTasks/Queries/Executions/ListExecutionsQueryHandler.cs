using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.Executor.Application.Abstractions.Persistence;
using Sentinel.Executor.Domain.Executions;

namespace Sentinel.Executor.Application.BusinessTasks.Queries.Executions;

/// <summary>The most recent execution requests, at most 200, as their snapshots.</summary>
public sealed class ListExecutionsQueryHandler(IExecutionStore store) : IQueryHandler<ListExecutionsQuery, IReadOnlyList<ExecutionSnapshot>>
{
    public async Task<Result<IReadOnlyList<ExecutionSnapshot>>> Handle(ListExecutionsQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        IReadOnlyList<ExecutionRequest> recent = await store.RecentAsync(Math.Clamp(query.Limit, 1, 200), cancellationToken);
        return Result.Success<IReadOnlyList<ExecutionSnapshot>>([.. recent.Select(request => request.Snapshot())]);
    }
}
