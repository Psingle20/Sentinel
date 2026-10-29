using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.Gateway.Application.BusinessTasks.Queries.Incidents;
using Sentinel.Contracts.Primitives.Identifiers;
using System.Text.Json;

namespace Sentinel.Gateway.Application.BusinessProcesses.Incidents;

public sealed class ListIncidentAgentExecutionsProcessor(IQueryDispatcher queries) : IProcessor<ListIncidentAgentExecutionsRequest, JsonElement>
{
    public Task<Result<JsonElement>> Process(ListIncidentAgentExecutionsRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return queries.Dispatch(new ListIncidentAgentExecutionsQuery(request.IncidentId), cancellationToken);
    }
}
