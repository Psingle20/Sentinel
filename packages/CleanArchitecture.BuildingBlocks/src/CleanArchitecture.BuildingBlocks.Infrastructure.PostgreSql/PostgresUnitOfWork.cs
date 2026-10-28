using CleanArchitecture.BuildingBlocks.Infrastructure.Messaging;
using CleanArchitecture.BuildingBlocks.Integration;
using Npgsql;

namespace CleanArchitecture.BuildingBlocks.Infrastructure.PostgreSql;

/// <summary>
/// The scope's PostgreSQL transaction. Service repositories, including EF Core through
/// <c>Database.UseTransactionAsync</c>, join it so domain writes, the inbox record and outbox
/// messages commit together.
/// </summary>
public interface IPostgresTransaction
{
    /// <summary>Returns the scope's transaction, opening a connection and beginning it on first use.</summary>
    Task<NpgsqlTransaction> GetTransactionAsync(CancellationToken cancellationToken);
}

internal sealed class PostgresUnitOfWork(NpgsqlDataSource dataSource, IPendingOutbox outbox)
    : IUnitOfWork, IPostgresTransaction, IAsyncDisposable
{
    private NpgsqlConnection? _connection;
    private NpgsqlTransaction? _transaction;

    public async Task<NpgsqlTransaction> GetTransactionAsync(CancellationToken cancellationToken)
    {
        if (_transaction is not null)
        {
            return _transaction;
        }

        _connection ??= await dataSource.OpenConnectionAsync(cancellationToken);
        _transaction = await _connection.BeginTransactionAsync(cancellationToken);
        return _transaction;
    }

    public async Task CommitAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<OutboxRecord> pending = outbox.DequeuePending();
        if (pending.Count == 0 && _transaction is null)
        {
            return;
        }

        NpgsqlTransaction transaction = await GetTransactionAsync(cancellationToken);
        foreach (OutboxRecord record in pending)
        {
            await InsertOutboxAsync(transaction, record, cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        await transaction.DisposeAsync();
        _transaction = null;
    }

    // Disposing an uncommitted transaction rolls it back, discarding writes and pending messages.
    public async ValueTask DisposeAsync()
    {
        if (_transaction is not null)
        {
            await _transaction.DisposeAsync();
        }

        if (_connection is not null)
        {
            await _connection.DisposeAsync();
        }
    }

    private static async Task InsertOutboxAsync(NpgsqlTransaction transaction, OutboxRecord record, CancellationToken cancellationToken)
    {
        MessageEnvelope envelope = record.Envelope;
        await using var command = new NpgsqlCommand(PostgresMessagingSql.InsertOutbox, transaction.Connection, transaction);
        command.Parameters.AddWithValue("message_id", envelope.MessageId);
        command.Parameters.AddWithValue("stream", record.Stream);
        command.Parameters.AddWithValue("message_type", envelope.MessageType);
        command.Parameters.AddWithValue("schema_version", envelope.SchemaVersion);
        command.Parameters.AddWithValue("occurred_at", envelope.OccurredAt);
        command.Parameters.AddWithValue("producer", envelope.Producer);
        command.Parameters.AddWithValue("correlation_id", (object?)envelope.CorrelationId ?? DBNull.Value);
        command.Parameters.AddWithValue("causation_id", (object?)envelope.CausationId ?? DBNull.Value);
        command.Parameters.AddWithValue("headers", MessageSerializer.SerializeHeaders(envelope.Headers));
        command.Parameters.AddWithValue("payload", envelope.Payload);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
