using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.AgentWorker.Application.BusinessTasks.Queries.Executions;
using Sentinel.AgentWorker.Domain.Executions;
using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.AgentWorker.Application.BusinessProcesses.Executions;

public sealed class GetExecutionProcessor(IQueryDispatcher queries) : IProcessor<GetExecutionRequest, AgentExecutionRecord?>
{
    public Task<Result<AgentExecutionRecord?>> Process(GetExecutionRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return queries.Dispatch(new GetExecutionQuery(request.ExecutionId), cancellationToken);
    }
}
