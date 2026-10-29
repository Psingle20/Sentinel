using System.Text.Json;
using CleanArchitecture.BuildingBlocks.Infrastructure.PostgreSql;
using Npgsql;
using NpgsqlTypes;
using Sentinel.Contracts.Incidents;
using Sentinel.Contracts.Messaging;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Runtime.Application;

namespace Sentinel.Runtime.Infrastructure.Storage;

/// <summary>
/// Incident memory in Runtime's database (Phase 07): one record per incident, and the feature
/// index retrieval matches on. Every query is scoped to one tenant; a deleted record keeps only its
/// tombstone and is removed from the index in the same transaction.
/// </summary>
internal sealed class PostgresIncidentMemoryStore(IPostgresTransaction transaction, NpgsqlDataSource dataSource) : IIncidentMemoryStore
{
    public async Task SaveAsync(IncidentMemoryView memory, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(memory);

        NpgsqlTransaction current = await transaction.GetTransactionAsync(cancellationToken);
        await using var upsert = new NpgsqlCommand(
            """
            insert into runtime.incident_memory (incident_id, tenant_id, environment_id, status, recorded_at, retain_until, legal_hold, privacy_class, deleted_at, record)
            values (@incident_id, @tenant_id, @environment_id, @status, @recorded_at, @retain_until, @legal_hold, @privacy_class, null, @record)
            on conflict (incident_id) do update set
                status = excluded.status, recorded_at = excluded.recorded_at, retain_until = excluded.retain_until,
                legal_hold = excluded.legal_hold, privacy_class = excluded.privacy_class, record = excluded.record
            where runtime.incident_memory.deleted_at is null
            """,
            current.Connection,
            current);
        Bind(upsert, memory);
        if (await upsert.ExecuteNonQueryAsync(cancellationToken) == 0)
        {
            // The record was deleted; a deletion is never undone by a later save.
            return;
        }

        await ReplaceFeaturesAsync(current, memory, memory.Features, cancellationToken);
    }

    public async Task<IncidentMemoryView?> GetAsync(IncidentId incidentId, CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = dataSource.CreateCommand("select record from runtime.incident_memory where incident_id = @incident_id");
        command.Parameters.AddWithValue("incident_id", incidentId.Value);
        return await command.ExecuteScalarAsync(cancellationToken) is string json
            ? JsonSerializer.Deserialize<IncidentMemoryView>(json, SentinelJson.Options)
            : null;
    }

    public async Task<IReadOnlyList<IncidentMemoryView>> CandidatesAsync(TenantId tenantId, EnvironmentId? environmentId, IReadOnlyCollection<string> features, DateTimeOffset now, int limit, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(features);

        await using NpgsqlCommand command = dataSource.CreateCommand(
            """
            select m.record from runtime.incident_memory m
            where m.tenant_id = @tenant_id
              and (@environment_id::text is null or m.environment_id = @environment_id)
              and m.deleted_at is null
              and (m.retain_until > @now or m.legal_hold)
              and exists (
                  select 1 from runtime.incident_memory_features f
                  where f.tenant_id = m.tenant_id and f.incident_id = m.incident_id and f.feature = any(@features))
            order by m.recorded_at desc
            limit @limit
            """);
        command.Parameters.AddWithValue("tenant_id", tenantId.Value);
        command.Parameters.Add(new NpgsqlParameter("environment_id", NpgsqlDbType.Text) { Value = (object?)environmentId?.Value ?? DBNull.Value });
        command.Parameters.AddWithValue("now", now);
        command.Parameters.AddWithValue("features", features.ToArray());
        command.Parameters.AddWithValue("limit", limit);
        return await ReadAsync(command, cancellationToken);
    }

    public async Task<IReadOnlyList<IncidentMemoryView>> ExpiredAsync(DateTimeOffset now, int limit, CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = dataSource.CreateCommand(
            """
            select record from runtime.incident_memory
            where deleted_at is null and not legal_hold and retain_until <= @now
            order by retain_until
            limit @limit
            """);
        command.Parameters.AddWithValue("now", now);
        command.Parameters.AddWithValue("limit", limit);
        return await ReadAsync(command, cancellationToken);
    }

    public async Task DeleteAsync(IncidentMemoryView tombstone, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tombstone);

        NpgsqlTransaction current = await transaction.GetTransactionAsync(cancellationToken);
        await using var update = new NpgsqlCommand(
            "update runtime.incident_memory set record = @record, deleted_at = @deleted_at, legal_hold = false where incident_id = @incident_id",
            current.Connection,
            current);
        update.Parameters.AddWithValue("incident_id", tombstone.IncidentId.Value);
        update.Parameters.AddWithValue("deleted_at", tombstone.Retention.DeletedAt ?? tombstone.RecordedAt);
        update.Parameters.Add(new NpgsqlParameter("record", NpgsqlDbType.Jsonb) { Value = JsonSerializer.Serialize(tombstone, SentinelJson.Options) });
        await update.ExecuteNonQueryAsync(cancellationToken);
        await ReplaceFeaturesAsync(current, tombstone, [], cancellationToken);
    }

    private static async Task ReplaceFeaturesAsync(NpgsqlTransaction current, IncidentMemoryView memory, IReadOnlyList<string> features, CancellationToken cancellationToken)
    {
        await using var clear = new NpgsqlCommand("delete from runtime.incident_memory_features where incident_id = @incident_id", current.Connection, current);
        clear.Parameters.AddWithValue("incident_id", memory.IncidentId.Value);
        await clear.ExecuteNonQueryAsync(cancellationToken);
        if (features.Count == 0)
        {
            return;
        }

        await using var insert = new NpgsqlCommand(
            """
            insert into runtime.incident_memory_features (tenant_id, environment_id, feature, incident_id)
            select @tenant_id, @environment_id, feature, @incident_id from unnest(@features) as feature
            on conflict do nothing
            """,
            current.Connection,
            current);
        insert.Parameters.AddWithValue("tenant_id", memory.TenantId.Value);
        insert.Parameters.AddWithValue("environment_id", memory.EnvironmentId.Value);
        insert.Parameters.AddWithValue("incident_id", memory.IncidentId.Value);
        insert.Parameters.AddWithValue("features", features.ToArray());
        await insert.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void Bind(NpgsqlCommand command, IncidentMemoryView memory)
    {
        command.Parameters.AddWithValue("incident_id", memory.IncidentId.Value);
        command.Parameters.AddWithValue("tenant_id", memory.TenantId.Value);
        command.Parameters.AddWithValue("environment_id", memory.EnvironmentId.Value);
        command.Parameters.AddWithValue("status", memory.Status.ToString());
        command.Parameters.AddWithValue("recorded_at", memory.RecordedAt);
        command.Parameters.AddWithValue("retain_until", memory.Retention.RetainUntil);
        command.Parameters.AddWithValue("legal_hold", memory.Retention.LegalHold);
        command.Parameters.AddWithValue("privacy_class", memory.Retention.PrivacyClass);
        command.Parameters.Add(new NpgsqlParameter("record", NpgsqlDbType.Jsonb) { Value = JsonSerializer.Serialize(memory, SentinelJson.Options) });
    }

    private static async Task<IReadOnlyList<IncidentMemoryView>> ReadAsync(NpgsqlCommand command, CancellationToken cancellationToken)
    {
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        var records = new List<IncidentMemoryView>();
        while (await reader.ReadAsync(cancellationToken))
        {
            records.Add(JsonSerializer.Deserialize<IncidentMemoryView>(reader.GetString(0), SentinelJson.Options)!);
        }

        return records;
    }
}
