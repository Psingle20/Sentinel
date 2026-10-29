using System.Net.Http.Json;
using System.Text.Json;
using CleanArchitecture.BuildingBlocks;
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
using Sentinel.AgentWorker.Application.Investigations;
using Sentinel.AgentWorker.Application.Tools;
using Sentinel.AgentWorker.Domain.Executions;
using Sentinel.AgentWorker.Infrastructure.Models;
using Sentinel.Contracts.Investigations;
using Sentinel.Contracts.Messaging;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Contracts.Queries;
using Sentinel.AgentWorker.Application.Abstractions.Persistence;

namespace Sentinel.AgentWorker.Infrastructure.Persistence;

internal sealed class PostgresExecutionStore(IPostgresTransaction transaction, NpgsqlDataSource dataSource) : IExecutionStore
{
    public async Task SaveAsync(AgentExecutionRecord execution, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(execution);

        NpgsqlTransaction current = await transaction.GetTransactionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            insert into agentworker.executions (execution_id, tenant_id, environment_id, incident_id, investigation_id, investigator, shadow, result, record, started_at, completed_at)
            values (@execution_id, @tenant_id, @environment_id, @incident_id, @investigation_id, @investigator, @shadow, @result, @record, @started_at, @completed_at)
            on conflict do nothing
            """,
            current.Connection,
            current);
        command.Parameters.AddWithValue("execution_id", execution.AgentExecutionId.Value);
        command.Parameters.AddWithValue("tenant_id", execution.TenantId.Value);
        command.Parameters.AddWithValue("environment_id", execution.EnvironmentId.Value);
        command.Parameters.AddWithValue("incident_id", execution.IncidentId.Value);
        command.Parameters.AddWithValue("investigation_id", execution.InvestigationId.Value);
        command.Parameters.AddWithValue("investigator", execution.Investigator);
        command.Parameters.AddWithValue("shadow", execution.Shadow);
        command.Parameters.AddWithValue("result", execution.Result.ToString());
        command.Parameters.Add(new NpgsqlParameter("record", NpgsqlDbType.Jsonb) { Value = JsonSerializer.Serialize(execution, SentinelJson.Options) });
        command.Parameters.AddWithValue("started_at", execution.StartedAt);
        command.Parameters.AddWithValue("completed_at", execution.CompletedAt);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<AgentExecutionRecord?> GetAsync(AgentExecutionId executionId, CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = dataSource.CreateCommand("select record from agentworker.executions where execution_id = @execution_id");
        command.Parameters.AddWithValue("execution_id", executionId.Value);
        return await command.ExecuteScalarAsync(cancellationToken) is string json ? JsonSerializer.Deserialize<AgentExecutionRecord>(json, SentinelJson.Options) : null;
    }

    public async Task SaveInputsAsync(ExecutionInputs inputs, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(inputs);

        NpgsqlTransaction current = await transaction.GetTransactionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            insert into agentworker.execution_inputs (execution_id, command, responses, tool_catalog_version, captured_at)
            values (@execution_id, @command, @responses, @tool_catalog_version, @captured_at)
            on conflict do nothing
            """,
            current.Connection,
            current);
        command.Parameters.AddWithValue("execution_id", inputs.AgentExecutionId.Value);
        command.Parameters.Add(new NpgsqlParameter("command", NpgsqlDbType.Jsonb) { Value = inputs.Command });
        command.Parameters.Add(new NpgsqlParameter("responses", NpgsqlDbType.Jsonb) { Value = JsonSerializer.Serialize(inputs.Responses, SentinelJson.Options) });
        command.Parameters.AddWithValue("tool_catalog_version", inputs.ToolCatalogVersion);
        command.Parameters.AddWithValue("captured_at", inputs.CapturedAt);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<ExecutionInputs?> GetInputsAsync(AgentExecutionId executionId, CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = dataSource.CreateCommand(
            "select command, responses, tool_catalog_version, captured_at from agentworker.execution_inputs where execution_id = @execution_id");
        command.Parameters.AddWithValue("execution_id", executionId.Value);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new ExecutionInputs
        {
            AgentExecutionId = executionId,
            Command = reader.GetString(0),
            Responses = JsonSerializer.Deserialize<List<CapturedResponse>>(reader.GetString(1), SentinelJson.Options) ?? [],
            ToolCatalogVersion = reader.GetString(2),
            CapturedAt = await reader.GetFieldValueAsync<DateTimeOffset>(3, cancellationToken)
        };
    }

    /// <summary>Replays run outside any message handler, so each is committed on its own connection.</summary>
    public async Task SaveReplayAsync(Sentinel.Contracts.Incidents.InvestigationReplayResult replay, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(replay);

        await using NpgsqlCommand command = dataSource.CreateCommand(
            "insert into agentworker.replays (replay_id, execution_id, level, record, replayed_at) values (@replay_id, @execution_id, @level, @record, @replayed_at) on conflict do nothing");
        command.Parameters.AddWithValue("replay_id", replay.ReplayId);
        command.Parameters.AddWithValue("execution_id", replay.AgentExecutionId.Value);
        command.Parameters.AddWithValue("level", replay.Level.ToString());
        command.Parameters.Add(new NpgsqlParameter("record", NpgsqlDbType.Jsonb) { Value = JsonSerializer.Serialize(replay, SentinelJson.Options) });
        command.Parameters.AddWithValue("replayed_at", replay.ReplayedAt);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<AgentExecutionRecord>> ForIncidentAsync(IncidentId incidentId, CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = dataSource.CreateCommand("select record from agentworker.executions where incident_id = @incident_id order by started_at");
        command.Parameters.AddWithValue("incident_id", incidentId.Value);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        var executions = new List<AgentExecutionRecord>();
        while (await reader.ReadAsync(cancellationToken))
        {
            executions.Add(JsonSerializer.Deserialize<AgentExecutionRecord>(reader.GetString(0), SentinelJson.Options)!);
        }

        return executions;
    }
}
