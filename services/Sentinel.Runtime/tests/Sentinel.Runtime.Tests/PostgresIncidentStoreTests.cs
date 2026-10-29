using Sentinel.Contracts.Incidents;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Runtime.Application.Abstractions.Persistence;
using Sentinel.Runtime.Application.Projections;
using Sentinel.Runtime.Domain.Correlation;
using Sentinel.Runtime.Domain.Incidents;
using Sentinel.Runtime.Domain.Incidents.Events;
using Sentinel.Runtime.Infrastructure.Persistence;
using Shouldly;
using static Sentinel.Runtime.Tests.IncidentScenario;

namespace Sentinel.Runtime.Tests;

/// <summary>
/// Phase 03's event store and projections against a real PostgreSQL (ADR-004): the stream's order and payloads, the
/// optimistic concurrency that keeps one writer per version, and the projection queries correlation relies on. The
/// other Runtime tests use the in-memory store, which cannot prove the SQL.
/// </summary>
public sealed class PostgresIncidentStoreTests(RuntimePostgresFixture database) : IClassFixture<RuntimePostgresFixture>
{
    private static readonly EnvironmentId Reference = EnvironmentId.From("reference");

    private readonly TenantId _tenant = TenantId.From($"tenant-{Guid.NewGuid():N}"[..20]);

    [Fact]
    public async Task A_stream_reads_back_in_version_order_exactly_as_it_was_appended()
    {
        Incident incident = Detected();
        StoredEvent[] events = Pending(incident);
        await AppendAsync(incident, events);

        IReadOnlyList<StoredEvent> stored = await Store().ReadStreamAsync(incident.Id, CancellationToken.None);

        stored.Select(item => item.Version).ShouldBe(events.Select(item => item.Version));
        stored.Select(item => IncidentEventTypes.Serialize(item.Event)).ShouldBe(events.Select(item => IncidentEventTypes.Serialize(item.Event)));
        var rebuilt = Incident.Rehydrate(stored.Select(item => item.Event));
        rebuilt.Version.ShouldBe(incident.Version);
        rebuilt.State.ShouldBe(incident.State);
    }

    [Fact]
    public async Task A_writer_behind_the_stream_is_refused_and_the_stream_is_unchanged()
    {
        Incident incident = Detected();
        StoredEvent[] events = Pending(incident);
        await AppendAsync(incident, events);

        await Should.ThrowAsync<IncidentConcurrencyException>(() => AppendAsync(incident, events));

        (await Store().ReadStreamAsync(incident.Id, CancellationToken.None)).Count.ShouldBe(events.Length);
    }

    [Fact]
    public async Task Of_two_writers_appending_the_same_version_at_once_exactly_one_succeeds()
    {
        Incident incident = Detected();
        StoredEvent[] events = Pending(incident);
        await AppendAsync(incident, events[..1]);

        // Both writers read version 1 and append version 2 in transactions that overlap; the second waits on the
        // first's uncommitted row and is refused when it commits.
        await using var first = new SingleTransaction(database.DataSource);
        await using var second = new SingleTransaction(database.DataSource);
        await Store(first).AppendAsync(incident, 1, events[1..2], CancellationToken.None);
        Task competing = Store(second).AppendAsync(incident, 1, events[1..2], CancellationToken.None);
        await first.CommitAsync();

        await Should.ThrowAsync<IncidentConcurrencyException>(() => competing);
        IReadOnlyList<StoredEvent> stored = await Store().ReadStreamAsync(incident.Id, CancellationToken.None);
        stored.Select(item => item.Version).ShouldBe([1L, 2L]);
    }

    [Fact]
    public async Task Only_an_open_incident_is_found_and_only_for_its_tenant_environment_and_service()
    {
        Incident incident = Detected();
        StoredEvent[] events = Pending(incident);
        IncidentDetail detail = IncidentProjector.Project(events);
        await SaveProjectionAsync(detail, isOpen: true);

        (await Store().FindOpenAsync(_tenant, Reference, Checkout, CancellationToken.None)).ShouldBe(incident.Id);
        (await Store().FindOpenAsync(TenantId.From($"other-{Guid.NewGuid():N}"[..20]), Reference, Checkout, CancellationToken.None)).ShouldBeNull();
        (await Store().FindOpenAsync(_tenant, EnvironmentId.From("staging"), Checkout, CancellationToken.None)).ShouldBeNull();
        (await Store().FindOpenAsync(_tenant, Reference, Reporting, CancellationToken.None)).ShouldBeNull();

        await SaveProjectionAsync(Resolved(detail, Start.AddHours(1)), isOpen: false);

        (await Store().FindOpenAsync(_tenant, Reference, Checkout, CancellationToken.None)).ShouldBeNull();
    }

    [Fact]
    public async Task A_projection_is_replaced_by_its_newer_version_and_reads_back_whole()
    {
        Incident incident = Detected();
        StoredEvent[] events = Pending(incident);
        IncidentDetail first = IncidentProjector.Project(events[..1]);
        IncidentDetail latest = IncidentProjector.Project(events);
        await SaveProjectionAsync(first, isOpen: true);
        await SaveProjectionAsync(latest, isOpen: true);

        (await Store().ProjectionVersionAsync(incident.Id, CancellationToken.None)).ShouldBe(events.Length);
        IncidentDetail stored = (await Store().GetDetailAsync(incident.Id, CancellationToken.None)).ShouldNotBeNull();
        stored.Summary.StreamVersion.ShouldBe(latest.Summary.StreamVersion);
        stored.Summary.Status.ShouldBe(latest.Summary.Status);
        stored.Summary.Title.ShouldBe(latest.Summary.Title);
    }

    [Fact]
    public async Task Correlation_sees_open_incidents_and_those_resolved_since_the_cutoff_only()
    {
        IncidentDetail open = IncidentProjector.Project(Pending(Detected()));
        IncidentDetail recent = Resolved(IncidentProjector.Project(Pending(Detected())), Start.AddMinutes(50));
        IncidentDetail old = Resolved(IncidentProjector.Project(Pending(Detected())), Start.AddMinutes(-50));
        await SaveProjectionAsync(open, isOpen: true);
        await SaveProjectionAsync(recent, isOpen: false);
        await SaveProjectionAsync(old, isOpen: false);

        IReadOnlyList<IncidentContext> candidates = await Store().CorrelationCandidatesAsync(_tenant, Reference, Start, CancellationToken.None);

        candidates.Select(candidate => candidate.IncidentId).ShouldBe([open.Summary.IncidentId, recent.Summary.IncidentId], ignoreOrder: true);
        candidates.Single(candidate => candidate.IncidentId == open.Summary.IncidentId).IsOpen.ShouldBeTrue();
        candidates.Single(candidate => candidate.IncidentId == recent.Summary.IncidentId).IsOpen.ShouldBeFalse();
    }

    private PostgresIncidentStore Store(SingleTransaction? transaction = null) => new(transaction ?? new SingleTransaction(database.DataSource), database.DataSource);

    private async Task AppendAsync(Incident incident, StoredEvent[] events)
    {
        await using var transaction = new SingleTransaction(database.DataSource);
        await Store(transaction).AppendAsync(incident, events[0].Version - 1, events, CancellationToken.None);
        await transaction.CommitAsync();
    }

    private async Task SaveProjectionAsync(IncidentDetail detail, bool isOpen)
    {
        await using var transaction = new SingleTransaction(database.DataSource);
        await Store(transaction).SaveProjectionAsync(detail, isOpen, CancellationToken.None);
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

    private static IncidentDetail Resolved(IncidentDetail detail, DateTimeOffset at) =>
        detail with { Summary = detail.Summary with { Status = IncidentStatus.Resolved, UpdatedAt = at } };
}
