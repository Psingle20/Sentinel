using System.Text.Json;
using CleanArchitecture.BuildingBlocks.Infrastructure.PostgreSql;
using Npgsql;
using Sentinel.Contracts;
using Sentinel.Contracts.Messaging;
using Sentinel.Contracts.Observations;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Contracts.Queries;
using Sentinel.Ingestion.Application.Abstractions.Persistence;
using Sentinel.Ingestion.Infrastructure.Persistence;
using Shouldly;
using Testcontainers.PostgreSql;

namespace Sentinel.Ingestion.Tests;

/// <summary>
/// The query contract's paging against a real PostgreSQL (Phase 02): a limit truncates the answer with a continuation
/// token, and following the tokens returns every observation exactly once, including ones that share an event time.
/// </summary>
public sealed class PostgresObservationStoreTests : IAsyncLifetime
{
    private static readonly TenantId Tenant = TenantId.From("acme");
    private static readonly EnvironmentId Environment = EnvironmentId.From("reference");
    private static readonly DateTimeOffset Start = new(2026, 10, 29, 9, 0, 0, TimeSpan.Zero);

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:18.3").Build();
    private NpgsqlDataSource _dataSource = null!;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        _dataSource = NpgsqlDataSource.Create(_postgres.GetConnectionString());
        await new IngestionStoreInitializer(_dataSource).InitializeAsync(CancellationToken.None);
    }

    public async Task DisposeAsync()
    {
        await _dataSource.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    [Fact]
    public async Task Following_continuation_tokens_returns_every_observation_once_even_when_event_times_tie()
    {
        // 25 samples sharing a timestamp in threes, so most pages end in the middle of a tie.
        Observation[] stored = [.. Enumerable.Range(0, 25).Select(index => Sample(index, Start.AddSeconds(index - index % 3)))];
        await SaveAsync(stored);

        var seen = new List<ObservationId>();
        string? token = null;
        int pages = 0;
        do
        {
            ObservationPage page = await Store().QueryAsync(Query(limit: 4) with { ContinuationToken = token }, CancellationToken.None);
            page.Observations.Count.ShouldBeLessThanOrEqualTo(4);
            page.Truncated.ShouldBe(page.ContinuationToken is not null);
            seen.AddRange(page.Observations.Select(observation => observation.ObservationId));
            token = page.ContinuationToken;
            pages++;
        }
        while (token is not null && pages < 20);

        pages.ShouldBe(7);
        seen.ShouldBe([.. stored.OrderBy(observation => observation.EventTime).ThenBy(observation => observation.ObservationId.Value, StringComparer.Ordinal).Select(observation => observation.ObservationId)]);
    }

    [Fact]
    public async Task An_answer_within_the_limit_is_neither_truncated_nor_given_a_token()
    {
        await SaveAsync([.. Enumerable.Range(100, 3).Select(index => Sample(index, Start.AddMinutes(10).AddSeconds(index)))]);

        ObservationPage page = await Store().QueryAsync(Query(limit: 10) with { Range = new TimeRange(Start.AddMinutes(10), Start.AddMinutes(20)) }, CancellationToken.None);

        page.Observations.Count.ShouldBe(3);
        page.Truncated.ShouldBeFalse();
        page.ContinuationToken.ShouldBeNull();
    }

    [Fact]
    public async Task An_alert_query_answers_alerts_only_and_each_reads_back_whole()
    {
        var alert = new AlertObservation
        {
            ObservationId = ObservationId.From("prometheus:alert:CheckoutSloFastBurn:f1:1793178000:firing"),
            TenantId = Tenant,
            EnvironmentId = Environment,
            ServiceId = ServiceId.From("checkout-service"),
            SourceProvider = "prometheus",
            SourceResource = "alerts/CheckoutSloFastBurn",
            EventTime = Start.AddMinutes(30),
            IngestTime = Start.AddMinutes(30).AddSeconds(5),
            AlertName = "CheckoutSloFastBurn",
            State = "firing",
            Severity = "critical",
            ActiveAt = Start.AddMinutes(30),
            Value = 14.8,
            Summary = "Checkout is burning its error budget 14.8 times faster than it can sustain."
        };
        await SaveAsync([alert, Sample(200, Start.AddMinutes(30))]);

        ObservationPage page = await Store().QueryAsync(
            Query(limit: 10) with { Kind = ObservationKind.Alert, Filters = new Dictionary<string, string>(), Range = new TimeRange(Start.AddMinutes(30), Start.AddMinutes(31)) },
            CancellationToken.None);

        Observation stored = page.Observations.ShouldHaveSingleItem();
        JsonSerializer.Serialize(stored, SentinelJson.Options).ShouldBe(JsonSerializer.Serialize<Observation>(alert, SentinelJson.Options));
    }

    private PostgresObservationStore Store(IPostgresTransaction? transaction = null) => new(transaction ?? new SingleTransaction(_dataSource), _dataSource);

    private async Task SaveAsync(Observation[] observations)
    {
        await using var transaction = new SingleTransaction(_dataSource);
        (await Store(transaction).AddNewAsync(observations, CancellationToken.None)).Count.ShouldBe(observations.Length);
        await transaction.CommitAsync();
    }

    private static ObservationQuery Query(int limit) => new()
    {
        TenantId = Tenant,
        EnvironmentId = Environment,
        ServiceId = ServiceId.From("checkout-service"),
        Kind = ObservationKind.Metric,
        Range = new TimeRange(Start, Start.AddMinutes(5)),
        Filters = new Dictionary<string, string> { ["metricName"] = "redis_connections" },
        Limit = limit
    };

    private static MetricObservation Sample(int index, DateTimeOffset at) => new()
    {
        ObservationId = ObservationId.From($"prometheus:redis_connections:checkout-service:{index:D3}"),
        TenantId = Tenant,
        EnvironmentId = Environment,
        ServiceId = ServiceId.From("checkout-service"),
        SourceProvider = "prometheus",
        SourceResource = "redis_connections",
        EventTime = at,
        IngestTime = at.AddSeconds(5),
        MetricName = "redis_connections",
        Value = index
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
