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

internal static class ControlPlaneHttp
{
    public static async Task<ControlPlaneResponse> SendAsync(HttpClient http, HttpMethod method, string uri, object? body, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, new Uri(uri, UriKind.Relative))
        {
            Content = body is null ? null : JsonContent.Create(body, options: SentinelJson.Options)
        };
        using HttpResponseMessage response = await http.SendAsync(request, cancellationToken);
        string text = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return new ControlPlaneResponse((int)response.StatusCode, null, Describe(text) ?? response.ReasonPhrase);
        }

        return new ControlPlaneResponse((int)response.StatusCode, text.Length > 0 ? JsonDocument.Parse(text).RootElement.Clone() : null, null);
    }
    // The owning service's problem details carry its reasons in detail (or title); pass those on, not the raw JSON.
    private static string? Describe(string text)
    {
        if (text.Length == 0)
        {
            return null;
        }

        try
        {
            using var problem = JsonDocument.Parse(text);
            JsonElement root = problem.RootElement;
            if (root.ValueKind == JsonValueKind.Object)
            {
                foreach (string property in (string[])["detail", "title"])
                {
                    if (root.TryGetProperty(property, out JsonElement value) && value.ValueKind == JsonValueKind.String && value.GetString() is { Length: > 0 } described)
                    {
                        return described;
                    }
                }
            }
        }
        catch (JsonException)
        {
            // Not JSON: the text is the description.
        }

        return text;
    }
}
