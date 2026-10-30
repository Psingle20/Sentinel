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

/// <summary>
/// Each area's document at the service that owns it, over HTTP; Api's own area in process. Bodies
/// pass through unchanged, so every owner answers with its document state, history or problem.
/// </summary>
internal sealed class ConfigurationOwnersClient(IHttpClientFactory clients, ApiConfigurationService api, ManagedDocument<ApiConfigurationDocument> current) : IConfigurationOwners
{
    public async Task<ControlPlaneResponse> CurrentAsync(string area, CancellationToken cancellationToken) =>
        area == ConfigurationAreas.Api
            ? Ok(await api.CurrentAsync(cancellationToken))
            : await ControlPlaneHttp.SendAsync(Client(area), HttpMethod.Get, "/v1/configuration", null, cancellationToken);

    public async Task<ControlPlaneResponse> HistoryAsync(string area, CancellationToken cancellationToken) =>
        area == ConfigurationAreas.Api
            ? Ok(await api.HistoryAsync(cancellationToken))
            : await ControlPlaneHttp.SendAsync(Client(area), HttpMethod.Get, "/v1/configuration/history", null, cancellationToken);

    public async Task<ControlPlaneResponse> PublishAsync(string area, JsonElement document, string expectedVersion, string author, CancellationToken cancellationToken)
    {
        if (area != ConfigurationAreas.Api)
        {
            return await ControlPlaneHttp.SendAsync(Client(area), HttpMethod.Put, "/admin/configuration", new { document, expectedVersion, author }, cancellationToken);
        }

        ApiConfigurationDocument? parsed;
        try
        {
            parsed = document.Deserialize<ApiConfigurationDocument>(SentinelJson.Options);
        }
        catch (JsonException exception)
        {
            return new ControlPlaneResponse(400, null, $"The document does not match Api's configuration: {exception.Message}");
        }

        if (parsed is null)
        {
            return new ControlPlaneResponse(400, null, "The document is empty.");
        }

        CleanArchitecture.BuildingBlocks.Result<PublishedDocument<ApiConfigurationDocument>> published = await api.PublishAsync(parsed, expectedVersion, author, cancellationToken);
        if (published.IsFailure)
        {
            // As every owner answers a refused publication: a conflict is 409 and a document refused on its content 422.
            int status = published.Error.Type switch
            {
                CleanArchitecture.BuildingBlocks.ErrorType.Conflict => 409,
                CleanArchitecture.BuildingBlocks.ErrorType.Validation => 400,
                CleanArchitecture.BuildingBlocks.ErrorType.NotFound => 404,
                _ => 422
            };
            return new ControlPlaneResponse(status, null, published.Error.Description, published.Error.Code);
        }

        current.Set(published.Value);
        return Ok(published.Value.Version);
    }

    private static ControlPlaneResponse Ok<T>(T value) => new(200, JsonSerializer.SerializeToElement(value, SentinelJson.Options), null);

    private HttpClient Client(string area) => clients.CreateClient(area);
}
