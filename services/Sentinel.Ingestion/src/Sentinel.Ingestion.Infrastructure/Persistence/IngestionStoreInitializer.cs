using System.Globalization;
using System.Text;
using System.Text.Json;
using CleanArchitecture.BuildingBlocks.Infrastructure.Messaging;
using CleanArchitecture.BuildingBlocks.Infrastructure.PostgreSql;
using Npgsql;
using NpgsqlTypes;
using Sentinel.Contracts.Messaging;
using Sentinel.Contracts.Observations;
using Sentinel.Contracts.Queries;
using Sentinel.Ingestion.Domain.Sources;
using Sentinel.Ingestion.Application.ControlPlane;

namespace Sentinel.Ingestion.Infrastructure.Persistence;

/// <summary>Creates Ingestion's tables in its own database, before messaging and workers start.</summary>
internal sealed class IngestionStoreInitializer(NpgsqlDataSource dataSource) : IMessagingStoreInitializer
{
    /// <summary>Published versions of Ingestion's control-plane document (Phase 08).</summary>
    public const string ConfigurationTable = "ingestion.configuration_versions";

    private const string Schema = """
        select pg_advisory_xact_lock(hashtext('sentinel.ingestion.schema'));

        create schema if not exists ingestion;

        create table if not exists ingestion.observations (
            tenant_id text not null,
            environment_id text not null,
            observation_id text not null,
            kind text not null,
            service_id text not null,
            metric_name text,
            event_time timestamptz not null,
            ingest_time timestamptz not null,
            payload jsonb not null,
            primary key (tenant_id, environment_id, observation_id)
        );

        create index if not exists observations_by_kind_service_time
            on ingestion.observations (tenant_id, environment_id, kind, service_id, event_time);

        create index if not exists observations_by_metric_time
            on ingestion.observations (tenant_id, environment_id, metric_name, event_time)
            where metric_name is not null;

        create table if not exists ingestion.source_states (
            source_id text primary key,
            provider text not null,
            checkpoint timestamptz not null,
            watermark timestamptz not null,
            health text not null,
            last_error text,
            consecutive_failures integer not null,
            updated_at timestamptz not null
        );
        """;

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(Schema + PostgresVersionedDocumentStore<SourceConfigurationDocument>.CreateTableSql(ConfigurationTable), connection, transaction);
        await command.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }
}
