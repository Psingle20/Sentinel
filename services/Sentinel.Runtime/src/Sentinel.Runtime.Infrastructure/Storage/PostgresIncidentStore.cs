using System.Text.Json;
using CleanArchitecture.BuildingBlocks.Infrastructure.PostgreSql;
using Npgsql;
using NpgsqlTypes;
using Sentinel.Contracts.Incidents;
using Sentinel.Contracts.Messaging;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Runtime.Application;
using Sentinel.Runtime.Domain.Correlation;
using Sentinel.Runtime.Domain.Incidents;
using RecoveryEvaluated = Sentinel.Runtime.Domain.Incidents.RecoveryEvaluated;

namespace Sentinel.Runtime.Infrastructure.Storage;

/// <summary>
/// Persisted event type names. They are part of the stored stream, so a name never changes once
/// written; a new event shape gets a new name or an upcaster (Phase 07).
/// </summary>
internal static class IncidentEventTypes
{
    private static readonly Dictionary<string, Type> ByName = new(StringComparer.Ordinal)
    {
        ["incident.detected.v1"] = typeof(IncidentDetected),
        ["investigation.requested.v1"] = typeof(InvestigationRequested),
        ["investigation.reported.v1"] = typeof(InvestigationReported),
        ["change.updated.v1"] = typeof(ChangeUpdated),
        ["decision.made.v1"] = typeof(DecisionMade),
        ["plan.proposed.v1"] = typeof(PlanProposed),
        ["plan.approved.v1"] = typeof(PlanApproved),
        ["plan.rejected.v1"] = typeof(PlanRejected),
        ["approval.revoked.v1"] = typeof(ApprovalRevoked),
        ["execution.requested.v1"] = typeof(ExecutionRequested),
        ["execution.started.v1"] = typeof(ExecutionStarted),
        ["execution.reported.v1"] = typeof(ExecutionReported),
        ["recovery.scheduled.v1"] = typeof(RecoveryScheduled),
        ["recovery.evaluated.v1"] = typeof(RecoveryEvaluated),
        ["incident.resolved.v1"] = typeof(IncidentResolved),
        ["incident.escalated.v1"] = typeof(IncidentEscalated),
        ["investigation.timed-out.v1"] = typeof(InvestigationTimedOut),
        ["decision.superseded.v1"] = typeof(DecisionSuperseded),
        ["signal.linked.v1"] = typeof(SignalLinked),
        ["severity.changed.v1"] = typeof(SeverityChanged),
        ["incident.reopened.v1"] = typeof(IncidentReopened)
    };

    private static readonly Dictionary<Type, string> ByType = ByName.ToDictionary(pair => pair.Value, pair => pair.Key);

    public static string NameOf(IIncidentEvent @event) => ByType[@event.GetType()];

    public static IIncidentEvent Deserialize(string name, string payload) =>
        (IIncidentEvent)JsonSerializer.Deserialize(IncidentEventUpcasters.Upcast(name, payload), ByName[name], SentinelJson.Options)!;

    public static string Serialize(IIncidentEvent @event) => JsonSerializer.Serialize(@event, @event.GetType(), SentinelJson.Options);
}

/// <summary>
/// Upcasters for stored event payloads (Phase 07): a payload written by an older shape is completed
/// before it is deserialized, so the stream never has to be rewritten. Each upcaster is pure and
/// idempotent, and only fills what the older shape lacked.
/// </summary>
internal static class IncidentEventUpcasters
{
    public static string Upcast(string name, string payload) => name switch
    {
        "incident.detected.v1" => TriggerMetricFromTitle(payload),
        _ => payload
    };

    /// <summary>Streams detected before Phase 07 carry the trigger metric only in the title, <c>{service} {metric} at {value}</c>.</summary>
    private static string TriggerMetricFromTitle(string payload)
    {
        if (System.Text.Json.Nodes.JsonNode.Parse(payload) is not System.Text.Json.Nodes.JsonObject detected || detected["triggerMetric"] is not null)
        {
            return payload;
        }

        string[] words = (detected["title"]?.GetValue<string>() ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length < 4 || words[^2] != "at")
        {
            return payload;
        }

        detected["triggerMetric"] = words[1];
        return detected.ToJsonString();
    }
}

internal sealed class PostgresIncidentStore(IPostgresTransaction transaction, NpgsqlDataSource dataSource) : IIncidentStore
{
    private const string UniqueViolation = "23505";

    public async Task<IReadOnlyList<StoredEvent>> ReadStreamAsync(IncidentId incidentId, CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = dataSource.CreateCommand(
            "select version, event_type, payload from runtime.incident_events where incident_id = @incident_id order by version");
        command.Parameters.AddWithValue("incident_id", incidentId.Value);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        var events = new List<StoredEvent>();
        while (await reader.ReadAsync(cancellationToken))
        {
            events.Add(new StoredEvent(reader.GetInt64(0), IncidentEventTypes.Deserialize(reader.GetString(1), reader.GetString(2))));
        }

        return events;
    }

    public async Task AppendAsync(Incident incident, long expectedVersion, IReadOnlyList<StoredEvent> events, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(incident);
        ArgumentNullException.ThrowIfNull(events);

        NpgsqlTransaction current = await transaction.GetTransactionAsync(cancellationToken);
        try
        {
            foreach (StoredEvent stored in events)
            {
                await using var command = new NpgsqlCommand(
                    "insert into runtime.incident_events (incident_id, version, event_type, payload, occurred_at) values (@incident_id, @version, @event_type, @payload, @occurred_at)",
                    current.Connection,
                    current);
                command.Parameters.AddWithValue("incident_id", incident.Id.Value);
                command.Parameters.AddWithValue("version", stored.Version);
                command.Parameters.AddWithValue("event_type", IncidentEventTypes.NameOf(stored.Event));
                command.Parameters.Add(new NpgsqlParameter("payload", NpgsqlDbType.Jsonb) { Value = IncidentEventTypes.Serialize(stored.Event) });
                command.Parameters.AddWithValue("occurred_at", stored.Event.OccurredAt);
                await command.ExecuteNonQueryAsync(cancellationToken);
            }
        }
        catch (PostgresException exception) when (exception.SqlState == UniqueViolation)
        {
            throw new IncidentConcurrencyException(incident.Id, expectedVersion);
        }
    }

    public async Task SaveProjectionAsync(IncidentDetail detail, bool isOpen, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(detail);

        IncidentSummary summary = detail.Summary;
        NpgsqlTransaction current = await transaction.GetTransactionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            insert into runtime.incidents (incident_id, tenant_id, environment_id, service_id, status, is_open, opened_at, updated_at, version, summary, detail)
            values (@incident_id, @tenant_id, @environment_id, @service_id, @status, @is_open, @opened_at, @updated_at, @version, @summary, @detail)
            on conflict (incident_id) do update set
                status = excluded.status, is_open = excluded.is_open, updated_at = excluded.updated_at,
                version = excluded.version, summary = excluded.summary, detail = excluded.detail
            """,
            current.Connection,
            current);
        command.Parameters.AddWithValue("incident_id", summary.IncidentId.Value);
        command.Parameters.AddWithValue("tenant_id", summary.TenantId.Value);
        command.Parameters.AddWithValue("environment_id", summary.EnvironmentId.Value);
        command.Parameters.AddWithValue("service_id", summary.AffectedServiceId.Value);
        command.Parameters.AddWithValue("status", summary.Status.ToString());
        command.Parameters.AddWithValue("is_open", isOpen);
        command.Parameters.AddWithValue("opened_at", summary.OpenedAt);
        command.Parameters.AddWithValue("updated_at", summary.UpdatedAt);
        command.Parameters.AddWithValue("version", summary.StreamVersion);
        command.Parameters.Add(new NpgsqlParameter("summary", NpgsqlDbType.Jsonb) { Value = JsonSerializer.Serialize(summary, SentinelJson.Options) });
        command.Parameters.Add(new NpgsqlParameter("detail", NpgsqlDbType.Jsonb) { Value = JsonSerializer.Serialize(detail, SentinelJson.Options) });
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IncidentId?> FindOpenAsync(TenantId tenantId, EnvironmentId environmentId, ServiceId serviceId, CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = dataSource.CreateCommand(
            "select incident_id from runtime.incidents where tenant_id = @tenant_id and environment_id = @environment_id and service_id = @service_id and is_open");
        command.Parameters.AddWithValue("tenant_id", tenantId.Value);
        command.Parameters.AddWithValue("environment_id", environmentId.Value);
        command.Parameters.AddWithValue("service_id", serviceId.Value);
        return await command.ExecuteScalarAsync(cancellationToken) is Guid id ? IncidentId.From(id) : null;
    }

    public async Task<IReadOnlyList<IncidentContext>> CorrelationCandidatesAsync(TenantId tenantId, EnvironmentId environmentId, DateTimeOffset resolvedSince, CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = dataSource.CreateCommand(
            """
            select detail from runtime.incidents
            where tenant_id = @tenant_id and environment_id = @environment_id and (is_open or updated_at >= @since)
            """);
        command.Parameters.AddWithValue("tenant_id", tenantId.Value);
        command.Parameters.AddWithValue("environment_id", environmentId.Value);
        command.Parameters.AddWithValue("since", resolvedSince);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        var candidates = new List<IncidentContext>();
        while (await reader.ReadAsync(cancellationToken))
        {
            IncidentDetail detail = JsonSerializer.Deserialize<IncidentDetail>(reader.GetString(0), SentinelJson.Options)!;
            bool open = detail.Summary.Status != IncidentStatus.Resolved;
            candidates.Add(new IncidentContext(
                detail.Summary.IncidentId,
                detail.Summary.AffectedServiceId,
                detail.AffectedServices.Count > 0 ? detail.AffectedServices : [detail.Summary.AffectedServiceId],
                detail.Summary.SymptomOnset ?? detail.Summary.OpenedAt,
                open,
                open ? null : detail.Summary.UpdatedAt));
        }

        return candidates;
    }

    public async Task<long?> ProjectionVersionAsync(IncidentId incidentId, CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = dataSource.CreateCommand("select version from runtime.incidents where incident_id = @incident_id");
        command.Parameters.AddWithValue("incident_id", incidentId.Value);
        return await command.ExecuteScalarAsync(cancellationToken) is long version ? version : null;
    }

    public async Task<IReadOnlyList<IncidentSummary>> ListAsync(int limit, CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = dataSource.CreateCommand("select summary from runtime.incidents order by opened_at desc limit @limit");
        command.Parameters.AddWithValue("limit", limit);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        var summaries = new List<IncidentSummary>();
        while (await reader.ReadAsync(cancellationToken))
        {
            summaries.Add(JsonSerializer.Deserialize<IncidentSummary>(reader.GetString(0), SentinelJson.Options)!);
        }

        return summaries;
    }

    public async Task<IncidentDetail?> GetDetailAsync(IncidentId incidentId, CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = dataSource.CreateCommand("select detail from runtime.incidents where incident_id = @incident_id");
        command.Parameters.AddWithValue("incident_id", incidentId.Value);
        return await command.ExecuteScalarAsync(cancellationToken) is string json
            ? JsonSerializer.Deserialize<IncidentDetail>(json, SentinelJson.Options)
            : null;
    }

    public async Task<IReadOnlyList<IncidentId>> AllIncidentIdsAsync(CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = dataSource.CreateCommand("select distinct incident_id from runtime.incident_events");
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        var ids = new List<IncidentId>();
        while (await reader.ReadAsync(cancellationToken))
        {
            ids.Add(IncidentId.From(reader.GetGuid(0)));
        }

        return ids;
    }
}
