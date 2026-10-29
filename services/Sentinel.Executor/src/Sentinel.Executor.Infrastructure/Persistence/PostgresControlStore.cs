using System.Net.Http.Headers;
using System.Text.Json;
using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Infrastructure.Messaging;
using CleanArchitecture.BuildingBlocks.Infrastructure.PostgreSql;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using NpgsqlTypes;
using Sentinel.Contracts.Messaging;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Contracts.Remediation;
using Sentinel.Executor.Application;
using Sentinel.Executor.Domain.Capabilities;
using Sentinel.Executor.Domain.Executions;
using Sentinel.Executor.Domain.Policy;
using Sentinel.Executor.Application.Abstractions.Persistence;

namespace Sentinel.Executor.Infrastructure.Persistence;

/// <summary>Operator controls in Executor's own database, so an emergency stop survives restarts and needs no other service.</summary>
internal sealed class PostgresControlStore(IPostgresTransaction transaction, NpgsqlDataSource dataSource) : IControlStore
{
    private const string EmergencyStop = "emergency-stop";
    private const string DisabledPrefix = "disabled:";

    public async Task<ExecutorControls> GetAsync(CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = dataSource.CreateCommand("select control, value from executor.controls");
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        bool stopped = false;
        var disabled = new HashSet<string>(StringComparer.Ordinal);
        while (await reader.ReadAsync(cancellationToken))
        {
            string control = reader.GetString(0);
            bool on = JsonSerializer.Deserialize<bool>(reader.GetString(1));
            if (control == EmergencyStop)
            {
                stopped = on;
            }
            else if (control.StartsWith(DisabledPrefix, StringComparison.Ordinal) && on)
            {
                disabled.Add(control[DisabledPrefix.Length..]);
            }
        }

        return new ExecutorControls(stopped, disabled);
    }

    public Task SetEmergencyStopAsync(bool engaged, string actor, CancellationToken cancellationToken) => SetAsync(EmergencyStop, engaged, actor, cancellationToken);

    public Task SetActionDisabledAsync(string actionKind, bool disabled, string actor, CancellationToken cancellationToken) => SetAsync(DisabledPrefix + actionKind, disabled, actor, cancellationToken);

    public async Task<IReadOnlyList<ControlChange>> HistoryAsync(int limit, CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = dataSource.CreateCommand("select control, value, changed_by, changed_at from executor.control_changes order by change_id desc limit @limit");
        command.Parameters.AddWithValue("limit", limit);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        var changes = new List<ControlChange>();
        while (await reader.ReadAsync(cancellationToken))
        {
            changes.Add(new ControlChange(reader.GetString(0), JsonSerializer.Deserialize<bool>(reader.GetString(1)), reader.GetString(2), await reader.GetFieldValueAsync<DateTimeOffset>(3, cancellationToken)));
        }

        return changes;
    }

    /// <summary>
    /// Sets the control and appends its audit record in the same transaction (review finding F-008): a change that
    /// commits is always recorded with the operator who made it, and one whose record fails never takes effect.
    /// </summary>
    private async Task SetAsync(string control, bool value, string actor, CancellationToken cancellationToken)
    {
        NpgsqlTransaction current = await transaction.GetTransactionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            insert into executor.controls (control, value, changed_by, changed_at) values (@control, @value, @actor, now())
            on conflict (control) do update set value = excluded.value, changed_by = excluded.changed_by, changed_at = excluded.changed_at;
            insert into executor.control_changes (control, value, changed_by, changed_at) values (@control, @value, @actor, now());
            """,
            current.Connection,
            current);
        command.Parameters.AddWithValue("control", control);
        command.Parameters.Add(new NpgsqlParameter("value", NpgsqlDbType.Jsonb) { Value = JsonSerializer.Serialize(value) });
        command.Parameters.AddWithValue("actor", actor);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
