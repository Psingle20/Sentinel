using CleanArchitecture.BuildingBlocks.Infrastructure.Messaging;
using Npgsql;

namespace CleanArchitecture.BuildingBlocks.Infrastructure.PostgreSql;

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
