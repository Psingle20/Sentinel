using System.Text;
using System.Text.Json;
using DotNet.Testcontainers.Containers;
using Npgsql;
using Sentinel.Contracts.Incidents;
using Sentinel.Contracts.Messaging;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Runtime.Application.Abstractions.Persistence;
using Sentinel.Runtime.Application.Projections;
using Sentinel.Runtime.Domain.Incidents;
using Sentinel.Runtime.Domain.Incidents.Events;
using Sentinel.Runtime.Infrastructure.Persistence;
using Shouldly;
using Testcontainers.PostgreSql;
using static Sentinel.Runtime.Tests.IncidentScenario;

namespace Sentinel.Runtime.Tests;

/// <summary>
/// Phase 09 restore evidence for Runtime's database, which holds the incident truth (ADR-004): a <c>pg_dump</c> taken with
/// an incident in flight, loaded into a new, empty server, holds that incident's stream and projection as they were at
/// the dump and nothing written after it. The incident goes on from the restored copy, and a writer behind the restored
/// stream is still refused, so optimistic concurrency survives the restore.
/// </summary>
public sealed class RuntimeRestoreTests : IAsyncLifetime
{
    private static readonly EnvironmentId Reference = EnvironmentId.From("reference");

    private readonly PostgreSqlContainer _original = new PostgreSqlBuilder("postgres:18.3").Build();
    private readonly PostgreSqlContainer _restored = new PostgreSqlBuilder("postgres:18.3").Build();
    private readonly TenantId _tenant = TenantId.From($"tenant-{Guid.NewGuid():N}"[..20]);

    public Task InitializeAsync() => Task.WhenAll(_original.StartAsync(), _restored.StartAsync());

    public async Task DisposeAsync()
    {
        await _original.DisposeAsync();
        await _restored.DisposeAsync();
    }

    [Fact]
    public async Task A_dump_restored_into_a_new_server_carries_an_in_flight_incident_on_from_the_dump()
    {
        await using var original = NpgsqlDataSource.Create(_original.GetConnectionString());
        await new RuntimeSchema(original).InitializeAsync(CancellationToken.None);
        Incident incident = Detected();
        StoredEvent[] backedUp = Pending(incident);
        IncidentDetail projection = IncidentProjector.Project(backedUp);
        await AppendAsync(original, incident, backedUp);
        await SaveProjectionAsync(original, projection);

        ExecResult dump = await _original.ExecAsync(["pg_dump", "-U", "postgres", "-d", "postgres", "-f", "/tmp/runtime.sql"]);
        dump.ExitCode.ShouldBe(0, dump.Stderr);
        incident.ChangeSeverity(IncidentSeverity.High, "Written after the backup.", Start.AddSeconds(90));
        await AppendAsync(original, incident, Pending(incident));

        string backup = (await _original.ExecAsync(["cat", "/tmp/runtime.sql"])).Stdout;
        await _restored.CopyAsync(Encoding.UTF8.GetBytes(backup), "/tmp/runtime.sql");
        ExecResult load = await _restored.ExecAsync(["psql", "-U", "postgres", "-d", "postgres", "-q", "-v", "ON_ERROR_STOP=1", "-f", "/tmp/runtime.sql"]);
        load.ExitCode.ShouldBe(0, load.Stderr);

        await using var restored = NpgsqlDataSource.Create(_restored.GetConnectionString());
        IReadOnlyList<StoredEvent> stream = await Store(restored).ReadStreamAsync(incident.Id, CancellationToken.None);
        stream.Select(item => IncidentEventTypes.Serialize(item.Event)).ShouldBe(backedUp.Select(item => IncidentEventTypes.Serialize(item.Event)), "the stream is the dump's, without what was written after it");
        IncidentDetail restoredProjection = (await Store(restored).GetDetailAsync(incident.Id, CancellationToken.None)).ShouldNotBeNull();
        JsonSerializer.Serialize(restoredProjection, SentinelJson.Options).ShouldBe(JsonSerializer.Serialize(projection, SentinelJson.Options));
        (await Store(restored).FindOpenAsync(_tenant, Reference, Checkout, CancellationToken.None)).ShouldBe(incident.Id);

        var resumed = Incident.Rehydrate(stream.Select(item => item.Event));
        resumed.ChangeSeverity(IncidentSeverity.High, "Written after the restore.", Start.AddSeconds(120));
        StoredEvent[] next = Pending(resumed);
        next.ShouldHaveSingleItem().Version.ShouldBe(backedUp.Length + 1);
        await AppendAsync(restored, resumed, next);
        await Should.ThrowAsync<IncidentConcurrencyException>(() => AppendAsync(restored, resumed, next));
        (await Store(restored).ReadStreamAsync(incident.Id, CancellationToken.None)).Count.ShouldBe(backedUp.Length + 1);
    }

    private static PostgresIncidentStore Store(NpgsqlDataSource dataSource, SingleTransaction? transaction = null) =>
        new(transaction ?? new SingleTransaction(dataSource), dataSource);

    private static async Task AppendAsync(NpgsqlDataSource dataSource, Incident incident, StoredEvent[] events)
    {
        await using var transaction = new SingleTransaction(dataSource);
        await Store(dataSource, transaction).AppendAsync(incident, events[0].Version - 1, events, CancellationToken.None);
        await transaction.CommitAsync();
    }

    private static async Task SaveProjectionAsync(NpgsqlDataSource dataSource, IncidentDetail detail)
    {
        await using var transaction = new SingleTransaction(dataSource);
        await Store(dataSource, transaction).SaveProjectionAsync(detail, isOpen: true, CancellationToken.None);
        await transaction.CommitAsync();
    }

    private Incident Detected()
    {
        var incident = Incident.Detect(
            IncidentId.New(),
            _tenant,
            Reference,
            Checkout,
            "checkout-service slo_burn_rate at 14.8",
            IncidentSeverity.Critical,
            Start.AddSeconds(60),
            [ObservationId.From("prometheus:slo_burn_rate:checkout-service:1")],
            [Deployment("checkout-service", "v2.4.1", "v2.4.0", 0, 20, "d1")],
            Start.AddSeconds(70),
            triggerMetric: "slo_burn_rate");
        incident.RequestInvestigation(InvestigationId.New(), "Find the cause.", Start.AddSeconds(-840), Start.AddSeconds(70), Start.AddSeconds(70));
        return incident;
    }

    private static StoredEvent[] Pending(Incident incident)
    {
        IIncidentEvent[] pending = [.. incident.DequeueDomainEvents().Cast<IIncidentEvent>()];
        long expected = incident.Version - pending.Length;
        return [.. pending.Select((@event, index) => new StoredEvent(expected + index + 1, @event))];
    }
}
