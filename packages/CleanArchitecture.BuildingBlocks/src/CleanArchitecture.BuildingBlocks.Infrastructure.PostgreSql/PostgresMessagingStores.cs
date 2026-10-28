using CleanArchitecture.BuildingBlocks.Infrastructure.Messaging;
using Npgsql;

namespace CleanArchitecture.BuildingBlocks.Infrastructure.PostgreSql;

/// <summary>Outbox operations for the dispatcher. Each call runs in its own short transaction.</summary>
internal sealed class PostgresOutboxStore(NpgsqlDataSource dataSource) : IOutboxStore
{
    public async Task<IReadOnlyList<OutboxRecord>> ClaimDueAsync(int batchSize, TimeSpan lease, CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = dataSource.CreateCommand(PostgresMessagingSql.ClaimDue);
        command.Parameters.AddWithValue("batch_size", batchSize);
        command.Parameters.AddWithValue("lease", lease);

        var records = new List<OutboxRecord>();
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var envelope = new MessageEnvelope(
                reader.GetGuid(0),
                reader.GetString(2),
                reader.GetInt32(3),
                await reader.GetFieldValueAsync<DateTimeOffset>(4, cancellationToken),
                reader.GetString(5),
                await reader.IsDBNullAsync(6, cancellationToken) ? null : reader.GetString(6),
                await reader.IsDBNullAsync(7, cancellationToken) ? null : reader.GetString(7),
                MessageSerializer.DeserializeHeaders(reader.GetString(8)),
                reader.GetString(9));

            records.Add(new OutboxRecord(envelope, reader.GetString(1), reader.GetInt32(10)));
        }

        return records;
    }

    public async Task MarkPublishedAsync(Guid messageId, CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = dataSource.CreateCommand(PostgresMessagingSql.MarkPublished);
        command.Parameters.AddWithValue("message_id", messageId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task MarkFailedAsync(Guid messageId, TimeSpan retryAfter, string error, CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = dataSource.CreateCommand(PostgresMessagingSql.MarkFailed);
        command.Parameters.AddWithValue("message_id", messageId);
        command.Parameters.AddWithValue("retry_after", retryAfter);
        command.Parameters.AddWithValue("error", error);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<TimeSpan?> GetOldestPendingAgeAsync(CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = dataSource.CreateCommand(PostgresMessagingSql.OldestPendingAge);
        object? age = await command.ExecuteScalarAsync(cancellationToken);
        return age is TimeSpan value ? value : null;
    }
}

/// <summary>Records handled messages inside the scope's transaction, so the record commits with the handler's writes.</summary>
internal sealed class PostgresInboxStore(IPostgresTransaction transaction) : IInboxStore
{
    public async Task<bool> TryRecordAsync(string consumer, MessageEnvelope envelope, CancellationToken cancellationToken)
    {
        NpgsqlTransaction current = await transaction.GetTransactionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(PostgresMessagingSql.RecordInbox, current.Connection, current);
        command.Parameters.AddWithValue("consumer", consumer);
        command.Parameters.AddWithValue("message_id", envelope.MessageId);
        command.Parameters.AddWithValue("message_type", envelope.MessageType);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }
}

internal sealed class PostgresMessagingSchema(NpgsqlDataSource dataSource) : IMessagingStoreInitializer
{
    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(PostgresMessagingSql.CreateSchema, connection, transaction);
        await command.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }
}
