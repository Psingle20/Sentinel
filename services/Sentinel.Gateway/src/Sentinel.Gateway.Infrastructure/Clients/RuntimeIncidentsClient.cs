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

internal sealed class RuntimeIncidentsClient(HttpClient http) : IRuntimeIncidents
{
    public async Task<IReadOnlyList<IncidentSummary>> ListAsync(int limit, CancellationToken cancellationToken) =>
        await http.GetFromJsonAsync<List<IncidentSummary>>($"/v1/incidents?limit={limit}", SentinelJson.Options, cancellationToken) ?? [];

    public Task<IncidentDetail?> GetAsync(IncidentId incidentId, CancellationToken cancellationToken) =>
        GetOrNullAsync<IncidentDetail>($"/v1/incidents/{incidentId}", cancellationToken);

    public Task<IncidentRunRecord?> RunRecordAsync(IncidentId incidentId, CancellationToken cancellationToken) =>
        GetOrNullAsync<IncidentRunRecord>($"/v1/incidents/{incidentId}/run-record", cancellationToken);

    private async Task<TValue?> GetOrNullAsync<TValue>(string uri, CancellationToken cancellationToken)
        where TValue : class
    {
        using HttpResponseMessage response = await http.GetAsync(new Uri(uri, UriKind.Relative), cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<TValue>(SentinelJson.Options, cancellationToken);
    }
}
