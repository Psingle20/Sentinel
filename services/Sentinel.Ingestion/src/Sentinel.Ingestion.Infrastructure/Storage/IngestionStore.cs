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
using Sentinel.Ingestion.Application;
using Sentinel.Ingestion.Domain.Sources;

namespace Sentinel.Ingestion.Infrastructure.Storage;

/// <summary>Creates Ingestion's tables in its own database, before messaging and workers start.</summary>
internal sealed class IngestionStoreInitializer(NpgsqlDataSource dataSource) : IMessagingStoreInitializer
{
    /// <summary>Published versions of Ingestion's control-plane document (Phase 08).</summary>
    public const string ConfigurationTable = "ingestion.configuration_versions";

    private const string Schema = """
        select pg_advisory_xact_lock(hashtext('sentinel.ingestion.schema'));

        create schema if not exists ingestion;

        create table if not exists ingestion.observations (
            tenant_id text not null,
            environment_id text not null,
            observation_id text not null,
            kind text not null,
            service_id text not null,
            metric_name text,
            event_time timestamptz not null,
            ingest_time timestamptz not null,
            payload jsonb not null,
            primary key (tenant_id, environment_id, observation_id)
        );

        create index if not exists observations_by_kind_service_time
            on ingestion.observations (tenant_id, environment_id, kind, service_id, event_time);

        create index if not exists observations_by_metric_time
            on ingestion.observations (tenant_id, environment_id, metric_name, event_time)
            where metric_name is not null;

        create table if not exists ingestion.source_states (
            source_id text primary key,
            provider text not null,
            checkpoint timestamptz not null,
            watermark timestamptz not null,
            health text not null,
            last_error text,
            consecutive_failures integer not null,
            updated_at timestamptz not null
        );
        """;

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(Schema + PostgresVersionedDocumentStore<SourceConfigurationDocument>.CreateTableSql(ConfigurationTable), connection, transaction);
        await command.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }
}

internal sealed class PostgresObservationStore(IPostgresTransaction transaction, NpgsqlDataSource dataSource) : IObservationStore
{
    private const string Insert = """
        insert into ingestion.observations (tenant_id, environment_id, observation_id, kind, service_id, metric_name, event_time, ingest_time, payload)
        values (@tenant_id, @environment_id, @observation_id, @kind, @service_id, @metric_name, @event_time, @ingest_time, @payload)
        on conflict do nothing
        returning observation_id
        """;

    public async Task<IReadOnlyList<Observation>> AddNewAsync(IReadOnlyList<Observation> observations, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(observations);

        if (observations.Count == 0)
        {
            return [];
        }

        NpgsqlTransaction current = await transaction.GetTransactionAsync(cancellationToken);
        var added = new List<Observation>();
        foreach (Observation observation in observations.DistinctBy(observation => observation.ObservationId))
        {
            await using var command = new NpgsqlCommand(Insert, current.Connection, current);
            command.Parameters.AddWithValue("tenant_id", observation.TenantId.Value);
            command.Parameters.AddWithValue("environment_id", observation.EnvironmentId.Value);
            command.Parameters.AddWithValue("observation_id", observation.ObservationId.Value);
            command.Parameters.AddWithValue("kind", KindName(observation));
            command.Parameters.AddWithValue("service_id", observation.ServiceId.Value);
            command.Parameters.AddWithValue("metric_name", observation is MetricObservation metric ? metric.MetricName : DBNull.Value);
            command.Parameters.AddWithValue("event_time", observation.EventTime);
            command.Parameters.AddWithValue("ingest_time", observation.IngestTime);
            command.Parameters.Add(new NpgsqlParameter("payload", NpgsqlDbType.Jsonb) { Value = JsonSerializer.Serialize(observation, SentinelJson.Options) });
            if (await command.ExecuteScalarAsync(cancellationToken) is not null)
            {
                added.Add(observation);
            }
        }

        return added;
    }

    public async Task<ObservationPage> QueryAsync(ObservationQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var sql = new StringBuilder("""
            select payload, event_time, observation_id from ingestion.observations
            where tenant_id = @tenant_id and environment_id = @environment_id and kind = @kind
              and event_time >= @from and event_time < @to
            """);
        await using NpgsqlCommand command = dataSource.CreateCommand();
        command.Parameters.AddWithValue("tenant_id", query.TenantId.Value);
        command.Parameters.AddWithValue("environment_id", query.EnvironmentId.Value);
        command.Parameters.AddWithValue("kind", query.Kind.ToString().ToLowerInvariant());
        command.Parameters.AddWithValue("from", query.Range.From);
        command.Parameters.AddWithValue("to", query.Range.To);

        if (query.ServiceId is { } service)
        {
            sql.Append(" and service_id = @service_id");
            command.Parameters.AddWithValue("service_id", service.Value);
        }

        if (query.Filters.TryGetValue("metricName", out string? metric))
        {
            sql.Append(" and metric_name = @metric_name");
            command.Parameters.AddWithValue("metric_name", metric);
        }

        if (ContinuationToken.TryParse(query.ContinuationToken, out DateTimeOffset afterTime, out string? afterId))
        {
            sql.Append(" and (event_time, observation_id) > (@after_time, @after_id)");
            command.Parameters.AddWithValue("after_time", afterTime);
            command.Parameters.AddWithValue("after_id", afterId!);
        }

        sql.Append(" order by event_time, observation_id limit @limit");
        command.Parameters.AddWithValue("limit", query.Limit + 1);
        command.CommandText = sql.ToString();

        var rows = new List<(Observation Observation, DateTimeOffset EventTime, string Id)>();
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            Observation observation = JsonSerializer.Deserialize<Observation>(reader.GetString(0), SentinelJson.Options)!;
            rows.Add((observation, await reader.GetFieldValueAsync<DateTimeOffset>(1, cancellationToken), reader.GetString(2)));
        }

        bool truncated = rows.Count > query.Limit;
        if (truncated)
        {
            rows.RemoveAt(rows.Count - 1);
        }

        string? next = truncated ? ContinuationToken.Create(rows[^1].EventTime, rows[^1].Id) : null;
        return new ObservationPage([.. rows.Select(row => row.Observation)], truncated, next);
    }

    private static string KindName(Observation observation) => observation switch
    {
        MetricObservation => "metric",
        LogObservation => "log",
        TraceObservation => "trace",
        DeploymentObservation => "deployment",
        TopologyObservation => "topology",
        _ => throw new ArgumentOutOfRangeException(nameof(observation), observation.GetType().Name, "Unknown observation kind.")
    };
}

/// <summary>An opaque keyset cursor: the last row's event time and ID.</summary>
internal static class ContinuationToken
{
    public static string Create(DateTimeOffset eventTime, string observationId) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(string.Create(CultureInfo.InvariantCulture, $"{eventTime.UtcTicks}|{observationId}")));

    public static bool TryParse(string? token, out DateTimeOffset eventTime, out string? observationId)
    {
        eventTime = default;
        observationId = null;
        if (string.IsNullOrEmpty(token))
        {
            return false;
        }

        try
        {
            string[] parts = Encoding.UTF8.GetString(Convert.FromBase64String(token)).Split('|', 2);
            if (parts.Length != 2 || !long.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out long ticks))
            {
                return false;
            }

            eventTime = new DateTimeOffset(ticks, TimeSpan.Zero);
            observationId = parts[1];
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}

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
