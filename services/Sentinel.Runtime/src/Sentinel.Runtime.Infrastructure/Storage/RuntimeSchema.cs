using CleanArchitecture.BuildingBlocks.Infrastructure.Messaging;
using Npgsql;

namespace Sentinel.Runtime.Infrastructure.Storage;

/// <summary>
/// Runtime's tables in its own database: the incident event stream (authoritative), the
/// incident read model (a rebuildable projection) and correlation state, including dependency edges
/// observed in traces, and incident memory with its feature index (Phase 07). Recovery timers live in the incident workflow (Phase 03); databases created
/// before it keep an unused <c>runtime.recovery_checks</c> table, which is left in place, not dropped.
/// </summary>
internal sealed class RuntimeSchema(NpgsqlDataSource dataSource) : IMessagingStoreInitializer
{
    private const string Sql = """
        select pg_advisory_xact_lock(hashtext('sentinel.runtime.schema'));

        create schema if not exists runtime;

        create table if not exists runtime.incident_events (
            incident_id uuid not null,
            version bigint not null,
            event_type text not null,
            payload jsonb not null,
            occurred_at timestamptz not null,
            recorded_at timestamptz not null default now(),
            primary key (incident_id, version)
        );

        create table if not exists runtime.incidents (
            incident_id uuid primary key,
            tenant_id text not null,
            environment_id text not null,
            service_id text not null,
            status text not null,
            is_open boolean not null,
            opened_at timestamptz not null,
            updated_at timestamptz not null,
            version bigint not null,
            summary jsonb not null,
            detail jsonb not null
        );

        create unique index if not exists incidents_one_open_per_service
            on runtime.incidents (tenant_id, environment_id, service_id)
            where is_open;

        create table if not exists runtime.changes (
            tenant_id text not null,
            environment_id text not null,
            deployment_id text not null,
            service_id text not null,
            version text not null,
            previous_version text,
            change_kind text not null,
            started_at timestamptz not null,
            completed_at timestamptz,
            failed boolean not null,
            observation_ids text[] not null,
            primary key (tenant_id, environment_id, deployment_id)
        );

        create index if not exists changes_by_service_time on runtime.changes (tenant_id, environment_id, service_id, started_at);

        create table if not exists runtime.signal_streaks (
            tenant_id text not null,
            environment_id text not null,
            service_id text not null,
            metric_name text not null,
            state jsonb not null,
            primary key (tenant_id, environment_id, service_id, metric_name)
        );

        create table if not exists runtime.service_edges (
            tenant_id text not null,
            environment_id text not null,
            caller text not null,
            callee text not null,
            last_seen_at timestamptz not null,
            primary key (tenant_id, environment_id, caller, callee)
        );

        create table if not exists runtime.incident_memory (
            incident_id uuid primary key,
            tenant_id text not null,
            environment_id text not null,
            status text not null,
            recorded_at timestamptz not null,
            retain_until timestamptz not null,
            legal_hold boolean not null,
            privacy_class text not null,
            deleted_at timestamptz,
            record jsonb not null
        );

        create index if not exists incident_memory_by_retention on runtime.incident_memory (retain_until) where deleted_at is null and not legal_hold;

        create table if not exists runtime.incident_memory_features (
            tenant_id text not null,
            environment_id text not null,
            feature text not null,
            incident_id uuid not null,
            primary key (tenant_id, feature, incident_id)
        );

        create index if not exists incident_memory_features_by_incident on runtime.incident_memory_features (incident_id);
        """;

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(Sql, connection, transaction);
        await command.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }
}
