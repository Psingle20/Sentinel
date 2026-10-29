using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Infrastructure.Messaging;
using CleanArchitecture.BuildingBlocks.Infrastructure.PostgreSql;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using NpgsqlTypes;
using Sentinel.Gateway.Application.Live;
using Sentinel.Contracts;
using Sentinel.Contracts.Incidents;
using Sentinel.Contracts.Messaging;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Contracts.Remediation;
using Sentinel.Gateway.Application.Abstractions.Providers;
using Sentinel.Gateway.Application.Options;

namespace Sentinel.Gateway.Infrastructure.Notifications;

/// <summary>
/// A webhook channel. The body is signed with HMAC-SHA256 under a secret read from configuration
/// (<see cref="NotificationChannelOptions.SecretKey"/>), so the receiver can tell it came from Sentinel.
/// </summary>
public sealed class WebhookNotificationProvider(HttpClient http, IConfiguration configuration) : INotificationProvider
{
    public const string SignatureHeader = "X-Sentinel-Signature";
    public const string DeliveryHeader = "X-Sentinel-Delivery";

    public string Kind => "webhook";

    public async Task SendAsync(NotificationMessage message, NotificationChannelOptions channel, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(channel);

        Uri url = channel.Url ?? throw new HttpRequestException($"Webhook channel '{channel.Name}' has no URL.");
        byte[] body = JsonSerializer.SerializeToUtf8Bytes(message, SentinelJson.Options);
        using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = new ByteArrayContent(body) };
        request.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
        request.Headers.Add(DeliveryHeader, message.NotificationId.ToString());
        if (channel.SecretKey is { } key && configuration[key] is { Length: > 0 } secret)
        {
            request.Headers.Add(SignatureHeader, $"sha256={Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), body))}");
        }

        using HttpResponseMessage response = await http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
    }
}
