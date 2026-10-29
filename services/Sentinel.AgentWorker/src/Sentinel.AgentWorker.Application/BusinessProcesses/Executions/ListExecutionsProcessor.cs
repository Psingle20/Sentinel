using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.AgentWorker.Application.BusinessTasks.Queries.Executions;
using Sentinel.AgentWorker.Domain.Executions;
using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.AgentWorker.Application.BusinessProcesses.Executions;

public sealed class ListExecutionsProcessor(IQueryDispatcher queries) : IProcessor<ListExecutionsRequest, IReadOnlyList<AgentExecutionRecord>>
{
    public Task<Result<IReadOnlyList<AgentExecutionRecord>>> Process(ListExecutionsRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return queries.Dispatch(new ListExecutionsQuery(request.IncidentId), cancellationToken);
    }
}
