using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CleanArchitecture.BuildingBlocks.Configuration;
using CleanArchitecture.BuildingBlocks.Infrastructure.Configuration;
using CleanArchitecture.BuildingBlocks.Infrastructure.Messaging;
using CleanArchitecture.BuildingBlocks.Infrastructure.PostgreSql;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Npgsql;
using NpgsqlTypes;
using Sentinel.Gateway.Application.Analytics;
using Sentinel.Gateway.Application.Abstractions.Providers;
using Sentinel.Gateway.Application.ControlPlane;
using Sentinel.Gateway.Application.Live;
using Sentinel.Gateway.Application.Postmortems;
using Sentinel.Contracts.Incidents;
using Sentinel.Contracts.Messaging;
using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.Gateway.Infrastructure.Clients;

internal sealed class AgentExecutionsClient(HttpClient http) : IAgentExecutions
{
    public async Task<JsonElement> ForIncidentAsync(IncidentId incidentId, CancellationToken cancellationToken) =>
        await http.GetFromJsonAsync<JsonElement>($"/v1/executions?incidentId={incidentId}", SentinelJson.Options, cancellationToken);

    public async Task<JsonElement?> GetAsync(AgentExecutionId executionId, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await http.GetAsync(new Uri($"/v1/executions/{executionId}", UriKind.Relative), cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>(SentinelJson.Options, cancellationToken);
    }

    public async Task<AgentReplayResponse> ReplayAsync(AgentExecutionId executionId, InvestigationReplayRequest request, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await http.PostAsJsonAsync($"/v1/executions/{executionId}/replays", request, SentinelJson.Options, cancellationToken);
        return response.IsSuccessStatusCode
            ? new AgentReplayResponse(await response.Content.ReadFromJsonAsync<InvestigationReplayResult>(SentinelJson.Options, cancellationToken), (int)response.StatusCode, null)
            : new AgentReplayResponse(null, (int)response.StatusCode, await response.Content.ReadAsStringAsync(cancellationToken));
    }
}
