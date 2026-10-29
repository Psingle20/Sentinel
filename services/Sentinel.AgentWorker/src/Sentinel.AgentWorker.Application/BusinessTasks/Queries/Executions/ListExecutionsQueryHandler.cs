using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.AgentWorker.Application.Abstractions.Persistence;
using Sentinel.AgentWorker.Domain.Executions;

namespace Sentinel.AgentWorker.Application.BusinessTasks.Queries.Executions;

/// <summary>Every execution recorded for the incident, shadow runs included, as the store orders them.</summary>
public sealed class ListExecutionsQueryHandler(IExecutionStore executions) : IQueryHandler<ListExecutionsQuery, IReadOnlyList<AgentExecutionRecord>>
{
    public async Task<Result<IReadOnlyList<AgentExecutionRecord>>> Handle(ListExecutionsQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        return Result.Success(await executions.ForIncidentAsync(query.IncidentId, cancellationToken));
    }
}
