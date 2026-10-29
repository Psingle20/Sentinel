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
using Sentinel.Api.Application.Live;
using Sentinel.Contracts;
using Sentinel.Contracts.Incidents;
using Sentinel.Contracts.Messaging;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Contracts.Remediation;

namespace Sentinel.Api.Infrastructure;

public static class LiveInfrastructure
{
    /// <summary>Live updates and notifications (Phase 08): the event log, the notification outbox, its dispatcher and providers.</summary>
    public static IServiceCollection AddLiveUpdates(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<NotificationOptions>().Bind(configuration.GetSection(NotificationOptions.Section));
        services.AddScoped<ILiveEventStore, PostgresLiveEventStore>();
        services.AddScoped<INotificationStore, PostgresNotificationStore>();
        services.AddScoped<IncidentEventRecorder>();
        services.AddSingleton<INotificationProvider, LogNotificationProvider>();
        services.AddHttpClient<WebhookNotificationProvider>();
        services.AddSingleton<INotificationProvider>(provider => provider.GetRequiredService<WebhookNotificationProvider>());
        services.AddHostedService<NotificationDispatcher>();
        return services;
    }
}

/// <summary>Records one lifecycle message as a live event and enqueues the notifications it routes to, in the inbox's transaction.</summary>
public sealed class IncidentEventRecorder(ILiveEventStore events, INotificationStore notifications, IOptions<NotificationOptions> options, TimeProvider time)
{
    public async Task<Result> RecordAsync(LiveEventDraft draft, CancellationToken cancellationToken)
    {
        await events.AppendAsync(draft, cancellationToken);
        foreach (NotificationMessage message in NotificationRouter.Route(draft, options.Value, () => Guid.CreateVersion7(time.GetUtcNow())))
        {
            await notifications.EnqueueAsync(message, cancellationToken);
        }

        return Result.Success();
    }
}

public sealed class IncidentOpenedHandler(IncidentEventRecorder recorder) : IIntegrationMessageHandler<IncidentOpened>
{
    public Task<Result> HandleAsync(IncidentOpened message, MessageEnvelope envelope, CancellationToken cancellationToken) => recorder.RecordAsync(LiveEventMapper.From(message), cancellationToken);
}

public sealed class IncidentStatusChangedHandler(IncidentEventRecorder recorder) : IIntegrationMessageHandler<IncidentStatusChanged>
{
    public Task<Result> HandleAsync(IncidentStatusChanged message, MessageEnvelope envelope, CancellationToken cancellationToken) => recorder.RecordAsync(LiveEventMapper.From(message), cancellationToken);
}

public sealed class IncidentBlackboardUpdatedHandler(IncidentEventRecorder recorder) : IIntegrationMessageHandler<IncidentBlackboardUpdated>
{
    public Task<Result> HandleAsync(IncidentBlackboardUpdated message, MessageEnvelope envelope, CancellationToken cancellationToken) => recorder.RecordAsync(LiveEventMapper.From(message), cancellationToken);
}

public sealed class DecisionRecordedHandler(IncidentEventRecorder recorder) : IIntegrationMessageHandler<DecisionRecorded>
{
    public Task<Result> HandleAsync(DecisionRecorded message, MessageEnvelope envelope, CancellationToken cancellationToken) => recorder.RecordAsync(LiveEventMapper.From(message), cancellationToken);
}

public sealed class RemediationPlanProposedHandler(IncidentEventRecorder recorder) : IIntegrationMessageHandler<RemediationPlanProposed>
{
    public Task<Result> HandleAsync(RemediationPlanProposed message, MessageEnvelope envelope, CancellationToken cancellationToken) => recorder.RecordAsync(LiveEventMapper.From(message), cancellationToken);
}

public sealed class RecoveryEvaluatedHandler(IncidentEventRecorder recorder) : IIntegrationMessageHandler<RecoveryEvaluated>
{
    public Task<Result> HandleAsync(RecoveryEvaluated message, MessageEnvelope envelope, CancellationToken cancellationToken) => recorder.RecordAsync(LiveEventMapper.From(message), cancellationToken);
}

/// <summary>
/// The live-event log. Sequences come from a Postgres sequence; a transaction that commits after a
/// later one could make a reader skip its event, so reads stop short of the last two seconds of
/// writes. A skipped event only delays an update: clients refetch the incident on every event.
/// </summary>
internal sealed class PostgresLiveEventStore(IPostgresTransaction transaction, NpgsqlDataSource dataSource) : ILiveEventStore
{
    public async Task AppendAsync(LiveEventDraft draft, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(draft);

        NpgsqlTransaction current = await transaction.GetTransactionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            insert into api.live_events (tenant_id, environment_id, incident_id, kind, stream_version, summary, severity, occurred_at)
            values (@tenant_id, @environment_id, @incident_id, @kind, @stream_version, @summary, @severity, @occurred_at)
            """,
            current.Connection,
            current);
        command.Parameters.AddWithValue("tenant_id", draft.TenantId.Value);
        command.Parameters.AddWithValue("environment_id", draft.EnvironmentId.Value);
        command.Parameters.AddWithValue("incident_id", draft.IncidentId.Value);
        command.Parameters.AddWithValue("kind", draft.Kind);
        command.Parameters.AddWithValue("stream_version", draft.StreamVersion);
        command.Parameters.AddWithValue("summary", draft.Summary);
        command.Parameters.Add(new NpgsqlParameter("severity", NpgsqlDbType.Text) { Value = (object?)draft.Severity?.ToString() ?? DBNull.Value });
        command.Parameters.AddWithValue("occurred_at", draft.OccurredAt);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<LiveEvent>> AfterAsync(long after, IReadOnlyCollection<TenantId> tenants, int limit, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tenants);

        await using NpgsqlCommand command = dataSource.CreateCommand(
            """
            select sequence, tenant_id, environment_id, incident_id, kind, stream_version, summary, severity, occurred_at
            from api.live_events
            where sequence > @after and tenant_id = any(@tenants) and recorded_at < now() - interval '2 seconds'
            order by sequence
            limit @limit
            """);
        command.Parameters.AddWithValue("after", after);
        command.Parameters.AddWithValue("tenants", tenants.Select(tenant => tenant.Value).ToArray());
        command.Parameters.AddWithValue("limit", limit);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        var events = new List<LiveEvent>();
        while (await reader.ReadAsync(cancellationToken))
        {
            events.Add(new LiveEvent(
                reader.GetInt64(0),
                TenantId.From(reader.GetString(1)),
                EnvironmentId.From(reader.GetString(2)),
                IncidentId.From(reader.GetGuid(3)),
                reader.GetString(4),
                reader.GetInt64(5),
                reader.GetString(6),
                await reader.IsDBNullAsync(7, cancellationToken) ? null : Enum.Parse<Severity>(reader.GetString(7)),
                await reader.GetFieldValueAsync<DateTimeOffset>(8, cancellationToken)));
        }

        return events;
    }

    public async Task<long> LatestAsync(CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = dataSource.CreateCommand("select coalesce(max(sequence), 0) from api.live_events");
        return (long)(await command.ExecuteScalarAsync(cancellationToken))!;
    }
}

internal sealed class PostgresNotificationStore(IPostgresTransaction transaction, NpgsqlDataSource dataSource) : INotificationStore
{
    public async Task EnqueueAsync(NotificationMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        NpgsqlTransaction current = await transaction.GetTransactionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            insert into api.notifications (notification_id, channel, status, attempts, next_attempt_at, message, created_at)
            values (@notification_id, @channel, 'Pending', 0, @created_at, @message, @created_at)
            on conflict do nothing
            """,
            current.Connection,
            current);
        command.Parameters.AddWithValue("notification_id", message.NotificationId);
        command.Parameters.AddWithValue("channel", message.Channel);
        command.Parameters.AddWithValue("created_at", message.OccurredAt);
        command.Parameters.Add(new NpgsqlParameter("message", NpgsqlDbType.Jsonb) { Value = JsonSerializer.Serialize(message, SentinelJson.Options) });
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<(NotificationMessage Message, int Attempts)>> DueAsync(DateTimeOffset now, int limit, CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = dataSource.CreateCommand(
            "select message, attempts from api.notifications where status = 'Pending' and next_attempt_at <= @now order by next_attempt_at limit @limit");
        command.Parameters.AddWithValue("now", now);
        command.Parameters.AddWithValue("limit", limit);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        var due = new List<(NotificationMessage, int)>();
        while (await reader.ReadAsync(cancellationToken))
        {
            due.Add((JsonSerializer.Deserialize<NotificationMessage>(reader.GetString(0), SentinelJson.Options)!, reader.GetInt32(1)));
        }

        return due;
    }

    /// <summary>Delivery runs outside any message handler, so each outcome is committed on its own.</summary>
    public async Task MarkAsync(Guid notificationId, NotificationStatus status, int attempts, string? error, DateTimeOffset? nextAttemptAt, CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = dataSource.CreateCommand(
            "update api.notifications set status = @status, attempts = @attempts, last_error = @error, next_attempt_at = coalesce(@next_attempt_at, next_attempt_at) where notification_id = @notification_id");
        command.Parameters.AddWithValue("notification_id", notificationId);
        command.Parameters.AddWithValue("status", status.ToString());
        command.Parameters.AddWithValue("attempts", attempts);
        command.Parameters.Add(new NpgsqlParameter("error", NpgsqlDbType.Text) { Value = (object?)error ?? DBNull.Value });
        command.Parameters.Add(new NpgsqlParameter("next_attempt_at", NpgsqlDbType.TimestampTz) { Value = (object?)nextAttemptAt ?? DBNull.Value });
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}

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

/// <summary>The local channel: notifications appear in Api's log and the Aspire dashboard.</summary>
internal sealed class LogNotificationProvider(ILogger<LogNotificationProvider> logger) : INotificationProvider
{
    public string Kind => "log";

    public Task SendAsync(NotificationMessage message, NotificationChannelOptions channel, CancellationToken cancellationToken)
    {
        logger.LogInformation("Notification to {Channel} for incident {IncidentId} ({Event}): {Text} {Link}", channel.Name, message.IncidentId, message.Event, message.Text, message.Link);
        return Task.CompletedTask;
    }
}

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
