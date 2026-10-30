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
        AlertObservation => "alert",
        _ => throw new ArgumentOutOfRangeException(nameof(observation), observation.GetType().Name, "Unknown observation kind.")
    };
}
