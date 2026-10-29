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

internal sealed class PostgresExecutionStore(IPostgresTransaction transaction, NpgsqlDataSource dataSource) : IExecutionStore
{
    public async Task<ExecutionRequest?> FindAsync(string idempotencyKey, CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = dataSource.CreateCommand("select snapshot from executor.execution_requests where idempotency_key = @key");
        command.Parameters.AddWithValue("key", idempotencyKey);
        return await command.ExecuteScalarAsync(cancellationToken) is string json ? Restore(json) : null;
    }

    public async Task SaveAsync(ExecutionRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        NpgsqlTransaction current = await transaction.GetTransactionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            insert into executor.execution_requests (request_id, idempotency_key, incident_id, state, final, snapshot, created_at, updated_at)
            values (@request_id, @key, @incident_id, @state, @final, @snapshot, @created_at, @updated_at)
            on conflict (request_id) do update set state = excluded.state, final = excluded.final, snapshot = excluded.snapshot, updated_at = excluded.updated_at
            """,
            current.Connection,
            current);
        command.Parameters.AddWithValue("request_id", request.RequestId);
        command.Parameters.AddWithValue("key", request.IdempotencyKey);
        command.Parameters.AddWithValue("incident_id", request.IncidentId.Value);
        command.Parameters.AddWithValue("state", request.State.ToString());
        command.Parameters.AddWithValue("final", request.HasFinalOutcome);
        command.Parameters.Add(new NpgsqlParameter("snapshot", NpgsqlDbType.Jsonb) { Value = JsonSerializer.Serialize(request.Snapshot(), SentinelJson.Options) });
        command.Parameters.AddWithValue("created_at", request.CreatedAt);
        command.Parameters.AddWithValue("updated_at", request.UpdatedAt);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ExecutionRequest>> UnfinishedAsync(DateTimeOffset olderThan, CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = dataSource.CreateCommand("select snapshot from executor.execution_requests where not final and updated_at < @older_than order by updated_at limit 20");
        command.Parameters.AddWithValue("older_than", olderThan);
        return await ReadAllAsync(command, cancellationToken);
    }

    public async Task<IReadOnlyList<ExecutionRequest>> RecentAsync(int limit, CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = dataSource.CreateCommand("select snapshot from executor.execution_requests order by created_at desc limit @limit");
        command.Parameters.AddWithValue("limit", limit);
        return await ReadAllAsync(command, cancellationToken);
    }

    public async Task<bool> TryConsumeAttestationAsync(AttestationId attestationId, Guid requestId, CancellationToken cancellationToken)
    {
        NpgsqlTransaction current = await transaction.GetTransactionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            "insert into executor.consumed_attestations (attestation_id, request_id) values (@attestation_id, @request_id) on conflict do nothing",
            current.Connection,
            current);
        command.Parameters.AddWithValue("attestation_id", attestationId.Value);
        command.Parameters.AddWithValue("request_id", requestId);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    public async Task<bool> TryRaiseFenceAsync(IncidentId incidentId, long fencingToken, CancellationToken cancellationToken)
    {
        NpgsqlTransaction current = await transaction.GetTransactionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            insert into executor.incident_fences (incident_id, highest_token) values (@incident_id, @token)
            on conflict (incident_id) do update set highest_token = excluded.highest_token
                where executor.incident_fences.highest_token <= excluded.highest_token
            """,
            current.Connection,
            current);
        command.Parameters.AddWithValue("incident_id", incidentId.Value);
        command.Parameters.AddWithValue("token", fencingToken);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    public async Task SaveCapabilityAsync(Capability capability, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(capability);

        NpgsqlTransaction current = await transaction.GetTransactionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            insert into executor.capabilities (capability_id, request_id, action, resource, scope, issued_at, expires_at, record)
            values (@id, @request_id, @action, @resource, @scope, @issued_at, @expires_at, @record)
            on conflict do nothing
            """,
            current.Connection,
            current);
        command.Parameters.AddWithValue("id", capability.CapabilityId);
        command.Parameters.AddWithValue("request_id", capability.RequestId);
        command.Parameters.AddWithValue("action", capability.Action);
        command.Parameters.AddWithValue("resource", capability.Resource);
        command.Parameters.AddWithValue("scope", capability.Scope);
        command.Parameters.AddWithValue("issued_at", capability.IssuedAt);
        command.Parameters.AddWithValue("expires_at", capability.ExpiresAt);
        command.Parameters.Add(new NpgsqlParameter("record", NpgsqlDbType.Jsonb) { Value = JsonSerializer.Serialize(capability, SentinelJson.Options) });
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<bool> TryConsumeCapabilityAsync(Guid capabilityId, CancellationToken cancellationToken)
    {
        NpgsqlTransaction current = await transaction.GetTransactionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            "update executor.capabilities set consumed_at = now() where capability_id = @id and consumed_at is null and expires_at > now()",
            current.Connection,
            current);
        command.Parameters.AddWithValue("id", capabilityId);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    private static async Task<IReadOnlyList<ExecutionRequest>> ReadAllAsync(NpgsqlCommand command, CancellationToken cancellationToken)
    {
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        var requests = new List<ExecutionRequest>();
        while (await reader.ReadAsync(cancellationToken))
        {
            requests.Add(Restore(reader.GetString(0)));
        }

        return requests;
    }

    private static ExecutionRequest Restore(string json) =>
        ExecutionRequest.Restore(JsonSerializer.Deserialize<ExecutionSnapshot>(json, SentinelJson.Options)!);
}
