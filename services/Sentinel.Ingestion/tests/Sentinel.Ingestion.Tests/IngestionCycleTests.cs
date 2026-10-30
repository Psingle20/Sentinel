using System.Net;
using System.Text;
using CleanArchitecture.BuildingBlocks.Integration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Sentinel.Contracts;
using Sentinel.Contracts.Observations;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Contracts.Queries;
using Sentinel.Ingestion.Application.Abstractions.Diagnostics;
using Sentinel.Ingestion.Application.Abstractions.Persistence;
using Sentinel.Ingestion.Application.Abstractions.Providers;
using Sentinel.Ingestion.Application.BusinessTasks.Queries;
using Sentinel.Ingestion.Application.BusinessTasks.Queries.Observations;
using Sentinel.Ingestion.Application.Options;
using Sentinel.Ingestion.Application.Services;
using Sentinel.Ingestion.Domain.Sources;
using Sentinel.Ingestion.Infrastructure.Providers;
using Shouldly;

namespace Sentinel.Ingestion.Tests;

public sealed class IngestionCycleTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 28, 9, 10, 3, TimeSpan.Zero);
    private static readonly MetricSeriesDefinition Burn = new() { Name = "slo_burn_rate", Expression = "slo:burn", Service = "checkout-service" };

    private readonly FakeTimeProvider _time = new(Now);
    private readonly InMemoryStore _store = new();
    private readonly InMemoryStates _states = new();
    private readonly RecordingOutbox _outbox = new();

    private IngestionCycle Cycle(IIngestionMetrics? metrics = null)
    {
        var options = new IngestionOptions { Tenant = "acme", Environment = "reference", InitialLookback = TimeSpan.FromMinutes(1) };
        foreach (ObservationKind kind in Enum.GetValues<ObservationKind>())
        {
            options.PublishKinds.Add(kind);
        }

        return new IngestionCycle(_store, _states, _outbox, new NoUnitOfWork(), _time, Microsoft.Extensions.Options.Options.Create(options), NullLogger<IngestionCycle>.Instance, metrics);
    }

    [Fact]
    public async Task A_pull_stores_and_publishes_new_observations_and_moves_the_checkpoint_to_a_step_boundary()
    {
        var provider = new FakeMetrics();

        int added = await Cycle().PullMetricsAsync(provider, Burn, CancellationToken.None);

        added.ShouldBe(provider.Returned);
        _outbox.Messages.Count.ShouldBe(added);
        SourceState state = _states.States.Values.Single();
        state.Checkpoint.ShouldBe(new DateTimeOffset(2026, 10, 28, 9, 9, 50, TimeSpan.Zero));
        state.Watermark.ShouldBe(state.Checkpoint);
        (provider.Requests.Single().Range.From.Second % 5).ShouldBe(0);
    }

    [Fact]
    public async Task Pulling_the_same_data_again_creates_no_duplicates()
    {
        var provider = new FakeMetrics();
        await Cycle().PullMetricsAsync(provider, Burn, CancellationToken.None);
        int published = _outbox.Messages.Count;

        // Rewind the checkpoint: the provider answers with the same samples and IDs.
        _states.States[_states.States.Keys.Single()] = _states.States.Values.Single() with { Checkpoint = Now.AddMinutes(-1) };
        (await Cycle().PullMetricsAsync(provider, Burn, CancellationToken.None)).ShouldBe(0);

        _outbox.Messages.Count.ShouldBe(published);
    }

    [Fact]
    public async Task An_outage_keeps_the_checkpoint_and_becomes_unavailable_after_three_failures()
    {
        var provider = new FakeMetrics { Down = true };

        for (int attempt = 0; attempt < 3; attempt++)
        {
            await Cycle().PullMetricsAsync(provider, Burn, CancellationToken.None);
            _time.Advance(SourceState.MaxBackoff);
        }

        SourceState state = _states.States.Values.Single();
        state.Health.ShouldBe(SourceHealth.Unavailable);
        state.Checkpoint.ShouldBe(state.Watermark);
        state.Checkpoint.ShouldBeLessThan(Now.AddSeconds(-30));
        _outbox.Messages.ShouldBeEmpty();
        provider.Requests.Count.ShouldBe(3);
    }

    [Fact]
    public async Task A_failing_source_backs_off_so_an_outage_costs_one_bounded_call_per_interval()
    {
        var provider = new FakeMetrics { Down = true };

        await Cycle().PullMetricsAsync(provider, Burn, CancellationToken.None);
        await Cycle().PullMetricsAsync(provider, Burn, CancellationToken.None);
        await Cycle().PullMetricsAsync(provider, Burn, CancellationToken.None);

        // The first failure is retried at once; the second opens the breaker for 10 s.
        provider.Requests.Count.ShouldBe(2);
        _states.States.Values.Single().NextAttemptAt.ShouldBe(Now.AddSeconds(10));

        _time.Advance(TimeSpan.FromSeconds(10));
        await Cycle().PullMetricsAsync(provider, Burn, CancellationToken.None);
        provider.Requests.Count.ShouldBe(3);
        _states.States.Values.Single().NextAttemptAt.ShouldBe(Now.AddSeconds(30));
    }

    [Fact]
    public async Task Query_metadata_reports_the_slowest_source_watermark_and_partial_answers()
    {
        _states.States["prometheus:slo_burn_rate"] = SourceState.Start("prometheus:slo_burn_rate", "prometheus", Now, TimeSpan.FromMinutes(5)).Advance(Now.AddSeconds(-10), Now);
        _states.States["prometheus:redis_connections"] = SourceState.Start("prometheus:redis_connections", "prometheus", Now, TimeSpan.FromMinutes(5)).Advance(Now.AddSeconds(-40), Now).MarkFailed("down", Now);
        QueryObservationsQueryHandler queries = Queries();

        ObservationQueryResult burn = (await queries.Handle(new QueryObservationsQuery(Query(new Dictionary<string, string> { ["metricName"] = "slo_burn_rate" })), CancellationToken.None)).Value;
        ObservationQueryResult all = (await queries.Handle(new QueryObservationsQuery(Query([])), CancellationToken.None)).Value;

        burn.Metadata.ObservedThrough.ShouldBe(Now.AddSeconds(-10));
        burn.Metadata.Completeness.ShouldBe(QueryCompleteness.Complete);
        all.Metadata.ObservedThrough.ShouldBe(Now.AddSeconds(-40));
        all.Metadata.Completeness.ShouldBe(QueryCompleteness.Partial);
        all.Metadata.ProviderHealth.ShouldHaveSingleItem().Status.ShouldBe(ProviderStatus.Degraded);
    }

    [Fact]
    public async Task Queries_are_tenant_scoped_and_need_a_forward_range()
    {
        QueryObservationsQueryHandler queries = Queries();

        (await queries.Handle(new QueryObservationsQuery(Query([]) with { TenantId = TenantId.From("globex") }), CancellationToken.None)).Error.ShouldBe(QueryErrors.UnknownScope);
        (await queries.Handle(new QueryObservationsQuery(Query([]) with { Range = new TimeRange(Now, Now.AddMinutes(-1)) }), CancellationToken.None)).Error.ShouldBe(QueryErrors.InvalidRange);
    }

    private QueryObservationsQueryHandler Queries()
    {
        IOptions<IngestionOptions> options = Microsoft.Extensions.Options.Options.Create(new IngestionOptions { Tenant = "acme", Environment = "reference" });
        return new QueryObservationsQueryHandler(_store, _states, new QueryBulkhead(options), _time, options);
    }

    private static ObservationQuery Query(Dictionary<string, string> filters) => new()
    {
        TenantId = TenantId.From("acme"),
        EnvironmentId = EnvironmentId.From("reference"),
        Kind = ObservationKind.Metric,
        Range = new TimeRange(Now.AddMinutes(-5), Now),
        Filters = filters
    };

    [Fact]
    public async Task Late_log_lines_inside_the_overlap_are_stored_once_and_marked_late()
    {
        var logs = new FakeLogs();
        var metrics = new RecordingMetrics();
        var source = new LogSourceDefinition { Name = "warnings", Query = "{service_name=\"checkout-service\"}" };
        logs.Lines.Add(("first", Now.AddSeconds(-50)));
        (await Cycle(metrics).PullLogsAsync(logs, source, CancellationToken.None)).ShouldBe(1);
        SourceState after = _states.States.Values.Single();

        // Loki indexed a line late: it is behind the watermark, but inside the range the next pull re-reads.
        _time.Advance(TimeSpan.FromSeconds(10));
        logs.Lines.Add(("indexed late", after.Watermark.AddSeconds(-5)));
        int added = await Cycle(metrics).PullLogsAsync(logs, source, CancellationToken.None);

        added.ShouldBe(1);
        ObservationRecorded late = _outbox.Messages.OfType<ObservationRecorded>().Last();
        late.Observation.Attributes["late"].ShouldBe("true");
        _outbox.Messages.OfType<ObservationRecorded>().First().Observation.Attributes.ContainsKey("late").ShouldBeFalse();
        metrics.Duplicates.ShouldBe(1);
        metrics.Late.ShouldBe(1);
        metrics.LastLag.ShouldBe(Now.AddSeconds(10) - _states.States.Values.Single().Watermark);
    }

    [Fact]
    public async Task Active_alerts_are_stored_once_while_they_stay_active_and_answer_alert_queries()
    {
        var alerts = new FakeAlerts();

        int first = await Cycle().PullAlertsAsync(alerts, CancellationToken.None);
        _time.Advance(TimeSpan.FromSeconds(5));
        int second = await Cycle().PullAlertsAsync(alerts, CancellationToken.None);
        alerts.State = "firing";
        _time.Advance(TimeSpan.FromSeconds(5));
        int fired = await Cycle().PullAlertsAsync(alerts, CancellationToken.None);

        first.ShouldBe(1);
        second.ShouldBe(0, "an alert still pending adds nothing");
        fired.ShouldBe(1, "the alert starting to fire is a new observation");
        SourceState source = _states.States[IngestionCycle.AlertsSourceId("prometheus")];
        source.Watermark.ShouldBe(Now.AddSeconds(10));
        ObservationQueryResult answer = (await Queries().Handle(new QueryObservationsQuery(Query([]) with { Kind = ObservationKind.Alert }), CancellationToken.None)).Value;
        answer.Metadata.ObservedThrough.ShouldBe(Now.AddSeconds(10), "only the alerts source is measured for an alert query");
        answer.Metadata.Completeness.ShouldBe(QueryCompleteness.Complete);
    }

    [Fact]
    public void Only_observation_kinds_with_event_consumers_are_published_by_default()
    {
        new IngestionOptions().EffectivePublishKinds.ShouldBe([ObservationKind.Metric, ObservationKind.Deployment, ObservationKind.Topology]);
    }

    [Fact]
    public async Task A_query_burst_beyond_the_bulkhead_is_refused_at_once_and_ingestion_is_not_held_up()
    {
        using var bulkhead = new QueryBulkhead(Microsoft.Extensions.Options.Options.Create(new IngestionOptions { QueryConcurrency = 2, QueryQueueLimit = 1, QueryQueueTimeout = TimeSpan.FromSeconds(30) }));
        var gate = new TaskCompletionSource<CleanArchitecture.BuildingBlocks.Result<int>>(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<CleanArchitecture.BuildingBlocks.Result<int>>[] running = [.. Enumerable.Range(0, 3).Select(index => bulkhead.RunAsync(token => gate.Task, CancellationToken.None))];

        CleanArchitecture.BuildingBlocks.Result<int> refused = await bulkhead.RunAsync(token => Task.FromResult(CleanArchitecture.BuildingBlocks.Result.Success(1)), CancellationToken.None);

        refused.Error.ShouldBe(QueryErrors.BulkheadFull);
        bulkhead.InFlight.ShouldBe(3);

        // The ingestion worker never passes the bulkhead: a pull completes while every query slot is taken.
        (await Cycle().PullMetricsAsync(new FakeMetrics(), Burn, CancellationToken.None)).ShouldBeGreaterThan(0);

        gate.SetResult(CleanArchitecture.BuildingBlocks.Result.Success(7));
        foreach (CleanArchitecture.BuildingBlocks.Result<int> result in await Task.WhenAll(running))
        {
            result.Value.ShouldBe(7);
        }

        bulkhead.InFlight.ShouldBe(0);
    }

    [Fact]
    public async Task A_query_that_waits_too_long_for_a_slot_is_refused()
    {
        using var bulkhead = new QueryBulkhead(Microsoft.Extensions.Options.Options.Create(new IngestionOptions { QueryConcurrency = 1, QueryQueueLimit = 4, QueryQueueTimeout = TimeSpan.FromMilliseconds(50) }));
        var gate = new TaskCompletionSource<CleanArchitecture.BuildingBlocks.Result<int>>(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<CleanArchitecture.BuildingBlocks.Result<int>> holding = bulkhead.RunAsync(token => gate.Task, CancellationToken.None);

        (await bulkhead.RunAsync(token => Task.FromResult(CleanArchitecture.BuildingBlocks.Result.Success(1)), CancellationToken.None)).Error.ShouldBe(QueryErrors.BulkheadFull);

        gate.SetResult(CleanArchitecture.BuildingBlocks.Result.Success(2));
        (await holding).Value.ShouldBe(2);
    }

    private sealed class FakeLogs : ILogsProvider
    {
        public List<(string Line, DateTimeOffset At)> Lines { get; } = [];

        public string Name => "loki";

        public Task<ProviderResult<LogObservation>> QueryRangeAsync(LogRangeRequest request, CancellationToken cancellationToken)
        {
            LogObservation[] found = [.. Lines
                .Where(line => line.At >= request.Range.From && line.At < request.Range.To)
                .Select(line => new LogObservation
                {
                    ObservationId = ObservationId.From($"loki:warnings:checkout-service:{line.At.ToUnixTimeMilliseconds()}"),
                    TenantId = request.TenantId,
                    EnvironmentId = request.EnvironmentId,
                    ServiceId = ServiceId.From("checkout-service"),
                    SourceProvider = "loki",
                    SourceResource = request.Source.Query,
                    EventTime = line.At,
                    IngestTime = Now,
                    Severity = LogSeverity.Warning,
                    Message = line.Line
                })];
            return Task.FromResult(new ProviderResult<LogObservation>(found, request.Range.To, false, ProviderStatus.Healthy));
        }
    }

    private sealed class RecordingMetrics : IIngestionMetrics
    {
        public int Duplicates { get; private set; }

        public int Late { get; private set; }

        public TimeSpan LastLag { get; private set; }

        public void Pulled(string sourceId, int added, int duplicates, int late, TimeSpan watermarkLag)
        {
            Duplicates += duplicates;
            Late += late;
            LastLag = watermarkLag;
        }

        public void SourceFailed(string sourceId, bool unavailable)
        {
        }
    }

    private sealed class FakeAlerts : IAlertProvider
    {
        public string State { get; set; } = "pending";

        public string Name => "prometheus";

        public Task<ProviderResult<AlertObservation>> GetActiveAlertsAsync(AlertRequest request, CancellationToken cancellationToken)
        {
            var alert = new AlertObservation
            {
                ObservationId = ObservationId.From($"prometheus:alert:CheckoutSloFastBurn:f1:1793178000:{State}"),
                TenantId = request.TenantId,
                EnvironmentId = request.EnvironmentId,
                ServiceId = ServiceId.From("checkout-service"),
                SourceProvider = "prometheus",
                SourceResource = "alerts/CheckoutSloFastBurn",
                EventTime = Now.AddSeconds(-20),
                IngestTime = request.At,
                AlertName = "CheckoutSloFastBurn",
                State = State,
                ActiveAt = Now.AddSeconds(-20)
            };
            return Task.FromResult(new ProviderResult<AlertObservation>([alert], request.At, false, ProviderStatus.Healthy));
        }
    }

    private sealed class FakeMetrics : IMetricsProvider
    {
        public bool Down { get; init; }

        public int Returned { get; private set; }

        public List<MetricRangeRequest> Requests { get; } = [];

        public string Name => "prometheus";

        public Task<ProviderResult<MetricObservation>> QueryRangeAsync(MetricRangeRequest request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            if (Down)
            {
                return Task.FromResult(ProviderResult<MetricObservation>.Unavailable(request.Range.From, "connection refused"));
            }

            MetricObservation[] samples = [.. Enumerable.Range(0, 3).Select(index => new MetricObservation
            {
                ObservationId = ObservationId.From($"prometheus:slo_burn_rate:checkout-service:{index}"),
                TenantId = request.TenantId,
                EnvironmentId = request.EnvironmentId,
                ServiceId = ServiceId.From("checkout-service"),
                SourceProvider = "prometheus",
                SourceResource = "slo:burn",
                EventTime = request.Range.From.AddSeconds(index * 5),
                IngestTime = Now,
                MetricName = "slo_burn_rate",
                Value = 0.1
            })];
            Returned = samples.Length;
            return Task.FromResult(new ProviderResult<MetricObservation>(samples, request.Range.To, false, ProviderStatus.Healthy));
        }
    }

    private sealed class InMemoryStore : IObservationStore
    {
        private readonly Dictionary<ObservationId, Observation> _observations = [];

        public Task<IReadOnlyList<Observation>> AddNewAsync(IReadOnlyList<Observation> observations, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Observation>>([.. observations.Where(observation => _observations.TryAdd(observation.ObservationId, observation))]);

        public Task<ObservationPage> QueryAsync(ObservationQuery query, CancellationToken cancellationToken) =>
            Task.FromResult(new ObservationPage([.. _observations.Values], false, null));
    }

    private sealed class InMemoryStates : ISourceStateStore
    {
        public Dictionary<string, SourceState> States { get; } = new(StringComparer.Ordinal);

        public Task<SourceState?> GetAsync(string sourceId, CancellationToken cancellationToken) => Task.FromResult(States.GetValueOrDefault(sourceId));

        public Task<IReadOnlyList<SourceState>> GetAllAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<SourceState>>([.. States.Values]);

        public Task SaveAsync(SourceState state, CancellationToken cancellationToken)
        {
            States[state.SourceId] = state;
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingOutbox : IOutbox
    {
        public List<object> Messages { get; } = [];

        public void Enqueue<TMessage>(TMessage message, MessageMetadata? metadata = null)
            where TMessage : class
        {
            metadata.ShouldNotBeNull().Headers.ShouldNotBeNull().ShouldContainKey("sentinel-tenant-id");
            Messages.Add(message);
        }
    }

    private sealed class NoUnitOfWork : IUnitOfWork
    {
        public Task CommitAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
