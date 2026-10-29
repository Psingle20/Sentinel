using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Messaging;
using Microsoft.Extensions.Options;
using Sentinel.Executor.Application.Abstractions.Persistence;
using Sentinel.Executor.Application.Options;
using Sentinel.Executor.Domain.Executions;

namespace Sentinel.Executor.Application.BusinessTasks.Queries.Execution;

/// <summary>Requests without a final outcome that nothing has touched for <see cref="ExecutorOptions.ReconcileAfter"/>.</summary>
public sealed class ListUnfinishedExecutionsQueryHandler(IExecutionStore store, TimeProvider time, IOptions<ExecutorOptions> options) : IQueryHandler<ListUnfinishedExecutionsQuery, IReadOnlyList<string>>
{
    public async Task<Result<IReadOnlyList<string>>> Handle(ListUnfinishedExecutionsQuery query, CancellationToken cancellationToken)
    {
        IReadOnlyList<ExecutionRequest> unfinished = await store.UnfinishedAsync(time.GetUtcNow() - options.Value.ReconcileAfter, cancellationToken);
        return Result.Success<IReadOnlyList<string>>([.. unfinished.Select(request => request.IdempotencyKey)]);
    }
}
