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

namespace Sentinel.Executor.Infrastructure.Persistence;

internal sealed class ExecutorSchema(NpgsqlDataSource dataSource) : IMessagingStoreInitializer
{
    private const string Sql = """
        select pg_advisory_xact_lock(hashtext('sentinel.executor.schema'));

        create schema if not exists executor;

        create table if not exists executor.execution_requests (
            request_id uuid primary key,
            idempotency_key text not null unique,
            incident_id uuid not null,
            state text not null,
            final boolean not null,
            snapshot jsonb not null,
            created_at timestamptz not null,
            updated_at timestamptz not null
        );

        create index if not exists execution_requests_unfinished on executor.execution_requests (updated_at) where not final;

        create table if not exists executor.consumed_attestations (
            attestation_id uuid primary key,
            request_id uuid not null,
            consumed_at timestamptz not null default now()
        );

        create table if not exists executor.incident_fences (
            incident_id uuid primary key,
            highest_token bigint not null
        );

        create table if not exists executor.capabilities (
            capability_id uuid primary key,
            request_id uuid not null,
            action text not null,
            resource text not null,
            scope text not null,
            issued_at timestamptz not null,
            expires_at timestamptz not null,
            consumed_at timestamptz,
            record jsonb not null
        );

        create table if not exists executor.policy_versions (
            version text primary key,
            document jsonb not null,
            author text not null,
            published_at timestamptz not null
        );

        create table if not exists executor.controls (
            control text primary key,
            value jsonb not null,
            changed_by text not null,
            changed_at timestamptz not null default now()
        );

        create table if not exists executor.control_changes (
            change_id bigserial primary key,
            control text not null,
            value jsonb not null,
            changed_by text not null,
            changed_at timestamptz not null
        );
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
