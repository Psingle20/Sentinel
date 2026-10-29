using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.AgentWorker.Application.Abstractions.Persistence;
using Sentinel.AgentWorker.Domain.Executions;

namespace Sentinel.AgentWorker.Application.BusinessTasks.Queries.Executions;

/// <summary>One execution's audit record, or nothing when no execution has the ID.</summary>
public sealed class GetExecutionQueryHandler(IExecutionStore executions) : IQueryHandler<GetExecutionQuery, AgentExecutionRecord?>
{
    public async Task<Result<AgentExecutionRecord?>> Handle(GetExecutionQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        return Result.Success(await executions.GetAsync(query.ExecutionId, cancellationToken));
    }
}
