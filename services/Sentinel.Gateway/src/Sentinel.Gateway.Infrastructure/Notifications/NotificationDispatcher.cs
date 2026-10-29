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
using Sentinel.Gateway.Application.Abstractions.Persistence;
using Sentinel.Gateway.Application.Abstractions.Providers;
using Sentinel.Gateway.Application.Options;

namespace Sentinel.Gateway.Infrastructure.Notifications;

/// <summary>Delivers pending notifications with exponential backoff; a notification that keeps failing is marked failed, never dropped silently.</summary>
internal sealed class NotificationDispatcher(
    IServiceScopeFactory scopes,
    IEnumerable<INotificationProvider> providers,
    IOptions<NotificationOptions> options,
    TimeProvider time,
    ILogger<NotificationDispatcher> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(2);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await DispatchAsync(stoppingToken);
            }
            catch (Exception exception) when (exception is NpgsqlException or InvalidOperationException)
            {
                logger.LogWarning(exception, "Notification dispatch failed; it is retried in {Interval}", Interval);
            }

            await Task.Delay(Interval, time, stoppingToken);
        }
    }

    public async Task DispatchAsync(CancellationToken cancellationToken)
    {
        NotificationOptions settings = options.Value;
        await using AsyncServiceScope scope = scopes.CreateAsyncScope();
        INotificationStore store = scope.ServiceProvider.GetRequiredService<INotificationStore>();
        foreach ((NotificationMessage message, int attempts) in await store.DueAsync(time.GetUtcNow(), 20, cancellationToken))
        {
            NotificationChannelOptions? channel = settings.Channels.FirstOrDefault(candidate => candidate.Name == message.Channel);
            INotificationProvider? provider = channel is null ? null : providers.FirstOrDefault(candidate => candidate.Kind == channel.Kind);
            if (channel is null || provider is null)
            {
                await store.MarkAsync(message.NotificationId, NotificationStatus.Failed, attempts, $"Channel '{message.Channel}' is not configured.", null, cancellationToken);
                continue;
            }

            try
            {
                await provider.SendAsync(message, channel, cancellationToken);
                await store.MarkAsync(message.NotificationId, NotificationStatus.Delivered, attempts + 1, null, null, cancellationToken);
            }
            catch (HttpRequestException exception)
            {
                int attempt = attempts + 1;
                bool exhausted = attempt >= settings.MaxAttempts;
                logger.LogWarning(exception, "Notification {NotificationId} to {Channel} failed (attempt {Attempt})", message.NotificationId, message.Channel, attempt);
                await store.MarkAsync(
                    message.NotificationId,
                    exhausted ? NotificationStatus.Failed : NotificationStatus.Pending,
                    attempt,
                    exception.Message,
                    exhausted ? null : time.GetUtcNow() + TimeSpan.FromSeconds(Math.Min(300, 5 * Math.Pow(2, attempt - 1))),
                    cancellationToken);
            }
        }
    }
}
