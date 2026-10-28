using System.Text.Json;
using CleanArchitecture.BuildingBlocks.Infrastructure.PostgreSql;
using Npgsql;
using NpgsqlTypes;
using Sentinel.Contracts.Messaging;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Runtime.Application;
using Sentinel.Runtime.Domain.Correlation;
using Sentinel.Runtime.Domain.Incidents;

namespace Sentinel.Runtime.Infrastructure.Storage;

internal sealed class PostgresCorrelationStore(IPostgresTransaction transaction, NpgsqlDataSource dataSource) : ICorrelationStore
{
    private const string ChangeColumns = "deployment_id, service_id, version, previous_version, change_kind, started_at, completed_at, failed, observation_ids";

    public async Task<SignalStreak?> GetStreakAsync(TenantId tenantId, EnvironmentId environmentId, ServiceId serviceId, string metricName, CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = dataSource.CreateCommand(
            "select state from runtime.signal_streaks where tenant_id = @tenant_id and environment_id = @environment_id and service_id = @service_id and metric_name = @metric_name");
        command.Parameters.AddWithValue("tenant_id", tenantId.Value);
        command.Parameters.AddWithValue("environment_id", environmentId.Value);
        command.Parameters.AddWithValue("service_id", serviceId.Value);
        command.Parameters.AddWithValue("metric_name", metricName);
        return await command.ExecuteScalarAsync(cancellationToken) is string json
            ? JsonSerializer.Deserialize<SignalStreak>(json, SentinelJson.Options)
            : null;
    }

    public async Task SaveStreakAsync(SignalStreak streak, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(streak);

        NpgsqlTransaction current = await transaction.GetTransactionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            insert into runtime.signal_streaks (tenant_id, environment_id, service_id, metric_name, state)
            values (@tenant_id, @environment_id, @service_id, @metric_name, @state)
            on conflict (tenant_id, environment_id, service_id, metric_name) do update set state = excluded.state
            """,
            current.Connection,
            current);
        command.Parameters.AddWithValue("tenant_id", streak.TenantId.Value);
        command.Parameters.AddWithValue("environment_id", streak.EnvironmentId.Value);
        command.Parameters.AddWithValue("service_id", streak.ServiceId.Value);
        command.Parameters.AddWithValue("metric_name", streak.MetricName);
        command.Parameters.Add(new NpgsqlParameter("state", NpgsqlDbType.Jsonb) { Value = JsonSerializer.Serialize(streak, SentinelJson.Options) });
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<LinkedChange> SaveChangeAsync(TenantId tenantId, EnvironmentId environmentId, LinkedChange change, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(change);

        NpgsqlTransaction current = await transaction.GetTransactionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            $"""
            insert into runtime.changes (tenant_id, environment_id, {ChangeColumns})
            values (@tenant_id, @environment_id, @deployment_id, @service_id, @version, @previous_version, @change_kind, @started_at, @completed_at, @failed, @observation_ids)
            on conflict (tenant_id, environment_id, deployment_id) do update set
                previous_version = coalesce(runtime.changes.previous_version, excluded.previous_version),
                completed_at = coalesce(excluded.completed_at, runtime.changes.completed_at),
                failed = runtime.changes.failed or excluded.failed,
                observation_ids = array(select distinct unnest(runtime.changes.observation_ids || excluded.observation_ids) order by 1)
            returning {ChangeColumns}
            """,
            current.Connection,
            current);
        command.Parameters.AddWithValue("tenant_id", tenantId.Value);
        command.Parameters.AddWithValue("environment_id", environmentId.Value);
        command.Parameters.AddWithValue("deployment_id", change.DeploymentId);
        command.Parameters.AddWithValue("service_id", change.ServiceId.Value);
        command.Parameters.AddWithValue("version", change.Version);
        command.Parameters.AddWithValue("previous_version", (object?)change.PreviousVersion ?? DBNull.Value);
        command.Parameters.AddWithValue("change_kind", change.ChangeKind);
        command.Parameters.AddWithValue("started_at", change.StartedAt);
        command.Parameters.AddWithValue("completed_at", (object?)change.CompletedAt ?? DBNull.Value);
        command.Parameters.AddWithValue("failed", change.Failed);
        command.Parameters.AddWithValue("observation_ids", change.ObservationIds.Select(id => id.Value).ToArray());
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        return await ReadChangeAsync(reader, cancellationToken);
    }

    public async Task<IReadOnlyList<LinkedChange>> GetChangesAsync(TenantId tenantId, EnvironmentId environmentId, IReadOnlyCollection<ServiceId> services, DateTimeOffset startedFrom, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(services);

        await using NpgsqlCommand command = dataSource.CreateCommand(
            $"""
            select {ChangeColumns} from runtime.changes
            where tenant_id = @tenant_id and environment_id = @environment_id and (cardinality(@services) = 0 or service_id = any(@services)) and started_at >= @from
            order by started_at
            """);
        command.Parameters.AddWithValue("tenant_id", tenantId.Value);
        command.Parameters.AddWithValue("environment_id", environmentId.Value);
        command.Parameters.AddWithValue("services", services.Select(service => service.Value).ToArray());
        command.Parameters.AddWithValue("from", startedFrom);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        var changes = new List<LinkedChange>();
        while (await reader.ReadAsync(cancellationToken))
        {
            changes.Add(await ReadChangeAsync(reader, cancellationToken));
        }

        return changes;
    }

    public async Task<LinkedChange?> GetChangeAsync(TenantId tenantId, EnvironmentId environmentId, string deploymentId, CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = dataSource.CreateCommand(
            $"select {ChangeColumns} from runtime.changes where tenant_id = @tenant_id and environment_id = @environment_id and deployment_id = @deployment_id");
        command.Parameters.AddWithValue("tenant_id", tenantId.Value);
        command.Parameters.AddWithValue("environment_id", environmentId.Value);
        command.Parameters.AddWithValue("deployment_id", deploymentId);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? await ReadChangeAsync(reader, cancellationToken) : null;
    }

    public async Task SaveEdgeAsync(TenantId tenantId, EnvironmentId environmentId, ServiceId caller, ServiceId callee, DateTimeOffset seenAt, CancellationToken cancellationToken)
    {
        NpgsqlTransaction current = await transaction.GetTransactionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            insert into runtime.service_edges (tenant_id, environment_id, caller, callee, last_seen_at)
            values (@tenant_id, @environment_id, @caller, @callee, @seen_at)
            on conflict (tenant_id, environment_id, caller, callee) do update set last_seen_at = greatest(runtime.service_edges.last_seen_at, excluded.last_seen_at)
            """,
            current.Connection,
            current);
        command.Parameters.AddWithValue("tenant_id", tenantId.Value);
        command.Parameters.AddWithValue("environment_id", environmentId.Value);
        command.Parameters.AddWithValue("caller", caller.Value);
        command.Parameters.AddWithValue("callee", callee.Value);
        command.Parameters.AddWithValue("seen_at", seenAt);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<(ServiceId Caller, ServiceId Callee)>> GetEdgesAsync(TenantId tenantId, EnvironmentId environmentId, DateTimeOffset seenSince, CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = dataSource.CreateCommand(
            "select caller, callee from runtime.service_edges where tenant_id = @tenant_id and environment_id = @environment_id and last_seen_at >= @since");
        command.Parameters.AddWithValue("tenant_id", tenantId.Value);
        command.Parameters.AddWithValue("environment_id", environmentId.Value);
        command.Parameters.AddWithValue("since", seenSince);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        var edges = new List<(ServiceId Caller, ServiceId Callee)>();
        while (await reader.ReadAsync(cancellationToken))
        {
            edges.Add((ServiceId.From(reader.GetString(0)), ServiceId.From(reader.GetString(1))));
        }

        return edges;
    }

    private static async Task<LinkedChange> ReadChangeAsync(NpgsqlDataReader reader, CancellationToken cancellationToken) => new()
    {
        DeploymentId = reader.GetString(0),
        ServiceId = ServiceId.From(reader.GetString(1)),
        Version = reader.GetString(2),
        PreviousVersion = await reader.IsDBNullAsync(3, cancellationToken) ? null : reader.GetString(3),
        ChangeKind = reader.GetString(4),
        StartedAt = await reader.GetFieldValueAsync<DateTimeOffset>(5, cancellationToken),
        CompletedAt = await reader.IsDBNullAsync(6, cancellationToken) ? null : await reader.GetFieldValueAsync<DateTimeOffset>(6, cancellationToken),
        Failed = reader.GetBoolean(7),
        ObservationIds = [.. (await reader.GetFieldValueAsync<string[]>(8, cancellationToken)).Select(ObservationId.From)]
    };
}
