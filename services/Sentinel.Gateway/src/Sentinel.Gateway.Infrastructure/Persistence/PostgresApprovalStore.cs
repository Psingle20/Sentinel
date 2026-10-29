using System.Text.Json;
using CleanArchitecture.BuildingBlocks.Infrastructure.PostgreSql;
using Npgsql;
using NpgsqlTypes;
using Sentinel.Gateway.Application.Abstractions.Persistence;
using Sentinel.Gateway.Domain.Approvals;
using Sentinel.Contracts.Messaging;
using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.Gateway.Infrastructure.Persistence;

/// <summary>Approvals in <c>api.approvals</c>, one row per plan version, the approval's snapshot as its JSON record.</summary>
internal sealed class PostgresApprovalStore(IPostgresTransaction transaction, NpgsqlDataSource dataSource) : IApprovalStore
{
    public async Task<Approval?> FindAsync(IncidentId incidentId, PlanId planId, int planVersion, CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = dataSource.CreateCommand(
            "select record from api.approvals where incident_id = @incident_id and plan_id = @plan_id and plan_version = @plan_version");
        command.Parameters.AddWithValue("incident_id", incidentId.Value);
        command.Parameters.AddWithValue("plan_id", planId.Value);
        command.Parameters.AddWithValue("plan_version", planVersion);
        return await command.ExecuteScalarAsync(cancellationToken) is string json && JsonSerializer.Deserialize<ApprovalSnapshot>(json, SentinelJson.Options) is { } snapshot
            ? Approval.Rehydrate(snapshot)
            : null;
    }

    public async Task SaveAsync(Approval approval, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(approval);

        ApprovalSnapshot record = approval.Snapshot();
        NpgsqlTransaction current = await transaction.GetTransactionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            insert into api.approvals (approval_id, incident_id, plan_id, plan_version, decision, record, decided_at)
            values (@approval_id, @incident_id, @plan_id, @plan_version, @decision, @record, @decided_at)
            on conflict (approval_id) do update set decision = excluded.decision, record = excluded.record
            """,
            current.Connection,
            current);
        command.Parameters.AddWithValue("approval_id", record.ApprovalId);
        command.Parameters.AddWithValue("incident_id", record.IncidentId.Value);
        command.Parameters.AddWithValue("plan_id", record.PlanId.Value);
        command.Parameters.AddWithValue("plan_version", record.PlanVersion);
        command.Parameters.AddWithValue("decision", record.Decision.ToString());
        command.Parameters.Add(new NpgsqlParameter("record", NpgsqlDbType.Jsonb) { Value = JsonSerializer.Serialize(record, SentinelJson.Options) });
        command.Parameters.AddWithValue("decided_at", record.DecidedAt);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}