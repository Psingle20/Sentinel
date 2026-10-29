using System.Text.Json;
using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.Gateway.Application.Abstractions.Providers;

namespace Sentinel.Gateway.Application.BusinessTasks.Queries.Incidents;

/// <summary>AgentWorker's records pass through unchanged; the incident's tenant was checked before the request was sent.</summary>
public sealed class ListIncidentAgentExecutionsQueryHandler(IAgentExecutions executions) : IQueryHandler<ListIncidentAgentExecutionsQuery, JsonElement>
{
    public async Task<Result<JsonElement>> Handle(ListIncidentAgentExecutionsQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        return Result.Success(await executions.ForIncidentAsync(query.IncidentId, cancellationToken));
    }
}
