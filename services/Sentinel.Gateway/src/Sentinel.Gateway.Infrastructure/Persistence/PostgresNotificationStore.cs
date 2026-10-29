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

namespace Sentinel.Gateway.Infrastructure.Persistence;

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
