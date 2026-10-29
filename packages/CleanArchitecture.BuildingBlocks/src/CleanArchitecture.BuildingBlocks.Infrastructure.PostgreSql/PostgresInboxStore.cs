using CleanArchitecture.BuildingBlocks.Infrastructure.Messaging;
using Npgsql;

namespace CleanArchitecture.BuildingBlocks.Infrastructure.PostgreSql;

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
