using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CleanArchitecture.BuildingBlocks.Configuration;
using CleanArchitecture.BuildingBlocks.Infrastructure.Configuration;
using CleanArchitecture.BuildingBlocks.Infrastructure.Messaging;
using CleanArchitecture.BuildingBlocks.Infrastructure.PostgreSql;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Npgsql;
using NpgsqlTypes;
using Sentinel.Gateway.Application.Analytics;
using Sentinel.Gateway.Application.Abstractions.Providers;
using Sentinel.Gateway.Application.ControlPlane;
using Sentinel.Gateway.Application.Live;
using Sentinel.Gateway.Application.Postmortems;
using Sentinel.Contracts.Incidents;
using Sentinel.Contracts.Messaging;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Gateway.Application.Abstractions.Persistence;

namespace Sentinel.Gateway.Infrastructure.Persistence;

internal sealed class PostgresAuditLog(IPostgresTransaction transaction, NpgsqlDataSource dataSource) : IAuditLog
{
    public async Task RecordAsync(string actor, string action, string target, object detail, DateTimeOffset occurredAt, CancellationToken cancellationToken)
    {
        NpgsqlTransaction current = await transaction.GetTransactionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            "insert into api.audit_log (actor, action, target, detail, occurred_at) values (@actor, @action, @target, @detail, @occurred_at)",
            current.Connection,
            current);
        command.Parameters.AddWithValue("actor", actor);
        command.Parameters.AddWithValue("action", action);
        command.Parameters.AddWithValue("target", target);
        command.Parameters.Add(new NpgsqlParameter("detail", NpgsqlDbType.Jsonb) { Value = JsonSerializer.Serialize(detail, SentinelJson.Options) });
        command.Parameters.AddWithValue("occurred_at", occurredAt);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<AuditEntry>> RecentAsync(int limit, CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = dataSource.CreateCommand("select sequence, actor, action, target, detail, occurred_at from api.audit_log order by sequence desc limit @limit");
        command.Parameters.AddWithValue("limit", limit);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        var entries = new List<AuditEntry>();
        while (await reader.ReadAsync(cancellationToken))
        {
            entries.Add(new AuditEntry(
                reader.GetInt64(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
                JsonDocument.Parse(reader.GetString(4)).RootElement.Clone(),
                await reader.GetFieldValueAsync<DateTimeOffset>(5, cancellationToken)));
        }

        return entries;
    }
}
