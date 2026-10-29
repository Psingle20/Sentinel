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

internal sealed class RuntimeHistoryClient(HttpClient http) : IRuntimeHistory
{
    public Task<Postmortem?> PostmortemAsync(IncidentId incidentId, CancellationToken cancellationToken) =>
        SendOrNullAsync<Postmortem>(HttpMethod.Get, $"/v1/incidents/{incidentId}/postmortem", cancellationToken);

    public async Task<IReadOnlyList<SimilarIncident>?> SimilarAsync(IncidentId incidentId, CancellationToken cancellationToken) =>
        await SendOrNullAsync<List<SimilarIncident>>(HttpMethod.Get, $"/v1/incidents/{incidentId}/similar", cancellationToken);

    public Task<DomainReplayResult?> ReplayAsync(IncidentId incidentId, CancellationToken cancellationToken) =>
        SendOrNullAsync<DomainReplayResult>(HttpMethod.Post, $"/v1/incidents/{incidentId}/replays/domain", cancellationToken);

    private async Task<TValue?> SendOrNullAsync<TValue>(HttpMethod method, string uri, CancellationToken cancellationToken)
        where TValue : class
    {
        using var request = new HttpRequestMessage(method, new Uri(uri, UriKind.Relative));
        using HttpResponseMessage response = await http.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<TValue>(SentinelJson.Options, cancellationToken);
    }
}
