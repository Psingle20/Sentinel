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
using Sentinel.AgentWorker.Application.ControlPlane;

namespace Sentinel.AgentWorker.Infrastructure.Persistence;

internal sealed class AgentWorkerSchema(NpgsqlDataSource dataSource) : IMessagingStoreInitializer
{
    /// <summary>Published versions of AgentWorker's control-plane document (Phase 08).</summary>
    public const string ConfigurationTable = "agentworker.configuration_versions";

    private const string Sql = """
        select pg_advisory_xact_lock(hashtext('sentinel.agentworker.schema'));

        create schema if not exists agentworker;

        create table if not exists agentworker.executions (
            execution_id uuid primary key,
            tenant_id text not null,
            environment_id text not null,
            incident_id uuid not null,
            investigation_id uuid not null,
            investigator text not null,
            shadow boolean not null,
            result text not null,
            record jsonb not null,
            started_at timestamptz not null,
            completed_at timestamptz not null
        );

        create index if not exists executions_by_incident on agentworker.executions (incident_id, started_at);

        create table if not exists agentworker.execution_inputs (
            execution_id uuid primary key,
            command jsonb not null,
            responses jsonb not null,
            tool_catalog_version text not null,
            captured_at timestamptz not null
        );

        create table if not exists agentworker.replays (
            replay_id uuid primary key,
            execution_id uuid not null,
            level text not null,
            record jsonb not null,
            replayed_at timestamptz not null
        );

        create index if not exists replays_by_execution on agentworker.replays (execution_id, replayed_at);
        """;

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(Sql + PostgresVersionedDocumentStore<ModelConfigurationDocument>.CreateTableSql(ConfigurationTable), connection, transaction);
        await command.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }
}
