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

namespace Sentinel.Gateway.Infrastructure.Persistence;

internal sealed class ApiSchema(NpgsqlDataSource dataSource) : IMessagingStoreInitializer
{
    /// <summary>Published versions of Api's control-plane document (Phase 08).</summary>
    public const string ConfigurationTable = "api.configuration_versions";

    private const string Sql = """
        select pg_advisory_xact_lock(hashtext('sentinel.api.schema'));

        create schema if not exists api;

        create table if not exists api.approvals (
            approval_id uuid primary key,
            incident_id uuid not null,
            plan_id uuid not null,
            plan_version integer not null,
            decision text not null,
            record jsonb not null,
            decided_at timestamptz not null,
            unique (incident_id, plan_id, plan_version)
        );

        create table if not exists api.approval_keys (
            key_id text primary key,
            x text not null,
            y text not null,
            not_before timestamptz not null,
            expires timestamptz
        );

        create table if not exists api.postmortem_narratives (
            incident_id uuid primary key,
            markdown text not null,
            version integer not null,
            edited_by text not null,
            edited_at timestamptz not null
        );

        create table if not exists api.live_events (
            sequence bigserial primary key,
            tenant_id text not null,
            environment_id text not null,
            incident_id uuid not null,
            kind text not null,
            stream_version bigint not null,
            summary text not null,
            severity text,
            occurred_at timestamptz not null,
            recorded_at timestamptz not null default now()
        );

        create index if not exists live_events_by_tenant on api.live_events (tenant_id, sequence);

        create table if not exists api.notifications (
            notification_id uuid primary key,
            channel text not null,
            status text not null,
            attempts integer not null,
            next_attempt_at timestamptz not null,
            last_error text,
            message jsonb not null,
            created_at timestamptz not null
        );

        create index if not exists notifications_due on api.notifications (next_attempt_at) where status = 'Pending';

        create table if not exists api.audit_log (
            sequence bigserial primary key,
            actor text not null,
            action text not null,
            target text not null,
            detail jsonb not null,
            occurred_at timestamptz not null
        );

        create table if not exists api.idempotency_keys (
            actor text not null,
            key text not null,
            request_hash text not null,
            status_code integer,
            content_type text,
            body text,
            created_at timestamptz not null,
            completed_at timestamptz,
            primary key (actor, key)
        );

        create table if not exists api.analytics_daily (
            day date not null,
            event text not null,
            dimensions text not null,
            count bigint not null,
            primary key (day, event, dimensions)
        );
        """;

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(Sql + PostgresVersionedDocumentStore<ApiConfigurationDocument>.CreateTableSql(ConfigurationTable), connection, transaction);
        await command.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }
}
