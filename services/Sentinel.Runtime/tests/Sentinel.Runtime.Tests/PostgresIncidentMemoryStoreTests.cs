using System.Text.Json;
using CleanArchitecture.BuildingBlocks.Infrastructure.PostgreSql;
using Npgsql;
using Sentinel.Contracts.Incidents;
using Sentinel.Contracts.Messaging;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Runtime.Infrastructure.Persistence;
using Shouldly;

namespace Sentinel.Runtime.Tests;

/// <summary>
/// Phase 07 retention, legal hold and privacy deletion in the SQL Runtime runs (ADR-019), against a real
/// PostgreSQL: the in-memory store the other tests use cannot prove the queries.
/// </summary>
public sealed class PostgresIncidentMemoryStoreTests(RuntimePostgresFixture database) : IClassFixture<RuntimePostgresFixture>
{
    private const string Feature = "cause:change:checkout-service";

    private static readonly DateTimeOffset Now = new(2026, 10, 28, 9, 0, 0, TimeSpan.Zero);

    private static readonly EnvironmentId Reference = EnvironmentId.From("reference");

    private readonly TenantId _tenant = TenantId.From($"tenant-{Guid.NewGuid():N}"[..20]);

    [Fact]
    public async Task Retrieval_is_tenant_and_environment_scoped_matches_a_feature_and_keeps_expired_memory_only_on_legal_hold()
    {
        IncidentMemoryView kept = Memory(Now.AddDays(400));
        IncidentMemoryView held = Memory(Now.AddDays(-1), legalHold: true);
        IncidentMemoryView expired = Memory(Now.AddDays(-1));
        IncidentMemoryView otherFeature = Memory(Now.AddDays(400), feature: "cause:saturation:order-service");
        IncidentMemoryView otherTenant = Memory(Now.AddDays(400)) with { TenantId = TenantId.From($"other-{Guid.NewGuid():N}"[..20]) };
        await SaveAsync(kept, held, expired, otherFeature, otherTenant);

        IReadOnlyList<IncidentMemoryView> found = await Store().CandidatesAsync(_tenant, Reference, [Feature], Now, 10, CancellationToken.None);
        IReadOnlyList<IncidentMemoryView> anyEnvironment = await Store().CandidatesAsync(_tenant, null, [Feature], Now, 10, CancellationToken.None);
        IReadOnlyList<IncidentMemoryView> otherEnvironment = await Store().CandidatesAsync(_tenant, EnvironmentId.From("staging"), [Feature], Now, 10, CancellationToken.None);

        found.Select(memory => memory.IncidentId).ShouldBe([kept.IncidentId, held.IncidentId], ignoreOrder: true);
        anyEnvironment.Select(memory => memory.IncidentId).ShouldBe([kept.IncidentId, held.IncidentId], ignoreOrder: true);
        otherEnvironment.ShouldBeEmpty();
    }

    [Fact]
    public async Task The_purge_selects_expired_memory_that_is_neither_held_nor_deleted_oldest_first()
    {
        // Older than anything the other tests store, so the first page of the shared database is this test's.
        IncidentMemoryView oldest = Memory(Now.AddDays(-1000));
        IncidentMemoryView older = Memory(Now.AddDays(-999));
        IncidentMemoryView held = Memory(Now.AddDays(-1001), legalHold: true);
        IncidentMemoryView deleted = Memory(Now.AddDays(-1002));
        IncidentMemoryView current = Memory(Now.AddDays(1));
        await SaveAsync(oldest, older, held, deleted, current);
        await DeleteAsync(deleted);

        IReadOnlyList<IncidentMemoryView> page = await Store().ExpiredAsync(Now, 2, CancellationToken.None);
        IReadOnlyList<IncidentMemoryView> all = await Store().ExpiredAsync(Now, 1000, CancellationToken.None);

        page.Select(memory => memory.IncidentId).ShouldBe([oldest.IncidentId, older.IncidentId]);
        all.Where(memory => memory.TenantId == _tenant).Select(memory => memory.IncidentId).ShouldBe([oldest.IncidentId, older.IncidentId]);
    }

    [Fact]
    public async Task A_deletion_leaves_a_tombstone_outside_the_feature_index_that_a_later_save_never_restores()
    {
        IncidentMemoryView memory = Memory(Now.AddDays(400));
        await SaveAsync(memory);

        await DeleteAsync(memory);
        await SaveAsync(memory);

        IncidentMemoryView stored = (await Store().GetAsync(memory.IncidentId, CancellationToken.None)).ShouldNotBeNull();
        stored.Retention.DeletedAt.ShouldBe(Now);
        stored.Features.ShouldBeEmpty();
        (await Store().CandidatesAsync(_tenant, Reference, [Feature], Now, 10, CancellationToken.None)).ShouldBeEmpty();
        (await FeatureRowsAsync(memory.IncidentId)).ShouldBe(0);
    }

    [Fact]
    public async Task A_saved_memory_reads_back_byte_for_byte()
    {
        IncidentMemoryView memory = Memory(Now.AddDays(400));
        await SaveAsync(memory);

        IncidentMemoryView stored = (await Store().GetAsync(memory.IncidentId, CancellationToken.None)).ShouldNotBeNull();

        JsonSerializer.Serialize(stored, SentinelJson.Options).ShouldBe(JsonSerializer.Serialize(memory, SentinelJson.Options));
    }

    private PostgresIncidentMemoryStore Store(IPostgresTransaction? transaction = null) => new(transaction ?? new SingleTransaction(database.DataSource), database.DataSource);

    private async Task SaveAsync(params IncidentMemoryView[] memories)
    {
        foreach (IncidentMemoryView memory in memories)
        {
            await using var transaction = new SingleTransaction(database.DataSource);
            await Store(transaction).SaveAsync(memory, CancellationToken.None);
            await transaction.CommitAsync();
        }
    }

    private async Task DeleteAsync(IncidentMemoryView memory)
    {
        IncidentMemoryView tombstone = memory with
        {
            Title = "Deleted",
            Features = [],
            Retention = memory.Retention with { LegalHold = false, DeletedAt = Now, DeletedBy = "privacy@example.test", DeletionReason = "Erasure request." }
        };
        await using var transaction = new SingleTransaction(database.DataSource);
        await Store(transaction).DeleteAsync(tombstone, CancellationToken.None);
        await transaction.CommitAsync();
    }

    private async Task<long> FeatureRowsAsync(IncidentId incidentId)
    {
        await using NpgsqlCommand command = database.DataSource.CreateCommand("select count(*) from runtime.incident_memory_features where incident_id = @incident_id");
        command.Parameters.AddWithValue("incident_id", incidentId.Value);
        return (long)(await command.ExecuteScalarAsync())!;
    }

    private IncidentMemoryView Memory(DateTimeOffset retainUntil, bool legalHold = false, string feature = Feature) => new()
    {
        IncidentId = IncidentId.From(Guid.NewGuid()),
        TenantId = _tenant,
        EnvironmentId = Reference,
        Title = "Checkout success SLO burning",
        Status = IncidentStatus.Resolved,
        OpenedAt = Now.AddDays(-2),
        ClosedAt = Now.AddDays(-2).AddHours(1),
        Cycle = 1,
        StreamVersion = 12,
        AffectedServices = [ServiceId.From("checkout-service")],
        Symptoms = [new SymptomSignature(ServiceId.From("checkout-service"), "slo_burn_rate")],
        Decision = DecisionOutcome.Diagnosed,
        Versions = new VersionManifest { SchemaVersion = 2, DecisionAlgorithm = "consensus-v1", PolicyVersion = "policy-test" },
        Retention = new RetentionMetadata(retainUntil, legalHold, "operational", "redaction-v1"),
        RecordedAt = Now.AddDays(-2).AddHours(1),
        Features = [feature, "outcome:resolved"]
    };

    /// <summary>One connection and transaction, as the building blocks' unit of work gives a request scope.</summary>
    private sealed class SingleTransaction(NpgsqlDataSource dataSource) : IPostgresTransaction, IAsyncDisposable
    {
        private NpgsqlConnection? _connection;
        private NpgsqlTransaction? _transaction;

        public async Task<NpgsqlTransaction> GetTransactionAsync(CancellationToken cancellationToken)
        {
            if (_transaction is null)
            {
                _connection = await dataSource.OpenConnectionAsync(cancellationToken);
                _transaction = await _connection.BeginTransactionAsync(cancellationToken);
            }

            return _transaction;
        }

        public async Task CommitAsync()
        {
            if (_transaction is not null)
            {
                await _transaction.CommitAsync();
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (_transaction is not null)
            {
                await _transaction.DisposeAsync();
            }

            if (_connection is not null)
            {
                await _connection.DisposeAsync();
            }
        }
    }
}
