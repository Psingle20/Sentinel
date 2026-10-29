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

/// <summary>
/// The live-event log. Sequences come from a Postgres sequence; a transaction that commits after a
/// later one could make a reader skip its event, so reads stop short of the last two seconds of
/// writes. A skipped event only delays an update: clients refetch the incident on every event.
/// </summary>
internal sealed class PostgresLiveEventStore(IPostgresTransaction transaction, NpgsqlDataSource dataSource) : ILiveEventStore
{
    public async Task AppendAsync(LiveEventDraft draft, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(draft);

        NpgsqlTransaction current = await transaction.GetTransactionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            insert into api.live_events (tenant_id, environment_id, incident_id, kind, stream_version, summary, severity, occurred_at)
            values (@tenant_id, @environment_id, @incident_id, @kind, @stream_version, @summary, @severity, @occurred_at)
            """,
            current.Connection,
            current);
        command.Parameters.AddWithValue("tenant_id", draft.TenantId.Value);
        command.Parameters.AddWithValue("environment_id", draft.EnvironmentId.Value);
        command.Parameters.AddWithValue("incident_id", draft.IncidentId.Value);
        command.Parameters.AddWithValue("kind", draft.Kind);
        command.Parameters.AddWithValue("stream_version", draft.StreamVersion);
        command.Parameters.AddWithValue("summary", draft.Summary);
        command.Parameters.Add(new NpgsqlParameter("severity", NpgsqlDbType.Text) { Value = (object?)draft.Severity?.ToString() ?? DBNull.Value });
        command.Parameters.AddWithValue("occurred_at", draft.OccurredAt);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<LiveEvent>> AfterAsync(long after, IReadOnlyCollection<TenantId> tenants, int limit, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tenants);

        await using NpgsqlCommand command = dataSource.CreateCommand(
            """
            select sequence, tenant_id, environment_id, incident_id, kind, stream_version, summary, severity, occurred_at
            from api.live_events
            where sequence > @after and tenant_id = any(@tenants) and recorded_at < now() - interval '2 seconds'
            order by sequence
            limit @limit
            """);
        command.Parameters.AddWithValue("after", after);
        command.Parameters.AddWithValue("tenants", tenants.Select(tenant => tenant.Value).ToArray());
        command.Parameters.AddWithValue("limit", limit);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        var events = new List<LiveEvent>();
        while (await reader.ReadAsync(cancellationToken))
        {
            events.Add(new LiveEvent(
                reader.GetInt64(0),
                TenantId.From(reader.GetString(1)),
                EnvironmentId.From(reader.GetString(2)),
                IncidentId.From(reader.GetGuid(3)),
                reader.GetString(4),
                reader.GetInt64(5),
                reader.GetString(6),
                await reader.IsDBNullAsync(7, cancellationToken) ? null : Enum.Parse<Severity>(reader.GetString(7)),
                await reader.GetFieldValueAsync<DateTimeOffset>(8, cancellationToken)));
        }

        return events;
    }

    public async Task<long> LatestAsync(CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = dataSource.CreateCommand("select coalesce(max(sequence), 0) from api.live_events");
        return (long)(await command.ExecuteScalarAsync(cancellationToken))!;
    }
}
