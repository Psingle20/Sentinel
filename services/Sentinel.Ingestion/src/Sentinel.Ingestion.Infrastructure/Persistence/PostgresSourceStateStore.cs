using System.Globalization;
using System.Text;
using System.Text.Json;
using CleanArchitecture.BuildingBlocks.Infrastructure.Messaging;
using CleanArchitecture.BuildingBlocks.Infrastructure.PostgreSql;
using Npgsql;
using NpgsqlTypes;
using Sentinel.Contracts.Messaging;
using Sentinel.Contracts.Observations;
using Sentinel.Contracts.Queries;
using Sentinel.Ingestion.Domain.Sources;
using Sentinel.Ingestion.Application.Abstractions.Persistence;

namespace Sentinel.Ingestion.Infrastructure.Persistence;

internal sealed class PostgresSourceStateStore(IPostgresTransaction transaction, NpgsqlDataSource dataSource) : ISourceStateStore
{
    private const string Columns = "source_id, provider, checkpoint, watermark, health, last_error, consecutive_failures, updated_at";

    public async Task<SourceState?> GetAsync(string sourceId, CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = dataSource.CreateCommand($"select {Columns} from ingestion.source_states where source_id = @source_id");
        command.Parameters.AddWithValue("source_id", sourceId);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? await ReadAsync(reader, cancellationToken) : null;
    }

    public async Task<IReadOnlyList<SourceState>> GetAllAsync(CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = dataSource.CreateCommand($"select {Columns} from ingestion.source_states order by source_id");
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        var states = new List<SourceState>();
        while (await reader.ReadAsync(cancellationToken))
        {
            states.Add(await ReadAsync(reader, cancellationToken));
        }

        return states;
    }

    public async Task SaveAsync(SourceState state, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(state);

        NpgsqlTransaction current = await transaction.GetTransactionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            $"""
            insert into ingestion.source_states ({Columns})
            values (@source_id, @provider, @checkpoint, @watermark, @health, @last_error, @consecutive_failures, @updated_at)
            on conflict (source_id) do update set
                checkpoint = excluded.checkpoint, watermark = excluded.watermark, health = excluded.health,
                last_error = excluded.last_error, consecutive_failures = excluded.consecutive_failures, updated_at = excluded.updated_at
            """,
            current.Connection,
            current);
        command.Parameters.AddWithValue("source_id", state.SourceId);
        command.Parameters.AddWithValue("provider", state.Provider);
        command.Parameters.AddWithValue("checkpoint", state.Checkpoint);
        command.Parameters.AddWithValue("watermark", state.Watermark);
        command.Parameters.AddWithValue("health", state.Health.ToString());
        command.Parameters.AddWithValue("last_error", (object?)state.LastError ?? DBNull.Value);
        command.Parameters.AddWithValue("consecutive_failures", state.ConsecutiveFailures);
        command.Parameters.AddWithValue("updated_at", state.UpdatedAt);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<SourceState> ReadAsync(NpgsqlDataReader reader, CancellationToken cancellationToken) => new()
    {
        SourceId = reader.GetString(0),
        Provider = reader.GetString(1),
        Checkpoint = await reader.GetFieldValueAsync<DateTimeOffset>(2, cancellationToken),
        Watermark = await reader.GetFieldValueAsync<DateTimeOffset>(3, cancellationToken),
        Health = Enum.Parse<SourceHealth>(reader.GetString(4)),
        LastError = await reader.IsDBNullAsync(5, cancellationToken) ? null : reader.GetString(5),
        ConsecutiveFailures = reader.GetInt32(6),
        UpdatedAt = await reader.GetFieldValueAsync<DateTimeOffset>(7, cancellationToken)
    };
}
