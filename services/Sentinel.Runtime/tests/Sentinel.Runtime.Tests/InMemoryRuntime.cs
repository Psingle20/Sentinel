using System.Collections.Concurrent;
using CleanArchitecture.BuildingBlocks.Integration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Sentinel.Contracts;
using Sentinel.Contracts.Incidents;
using Sentinel.Contracts.Observations;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Contracts.Queries;
using Sentinel.Contracts.Remediation;
using Sentinel.Runtime.Application;
using Sentinel.Runtime.Application.Incidents;
using Sentinel.Runtime.Application.Memory;
using Sentinel.Runtime.Domain.Correlation;
using Sentinel.Runtime.Domain.Incidents;
using Sentinel.Runtime.Application.Abstractions.Runtime;
using Sentinel.Runtime.Application.Abstractions.Persistence;
using Sentinel.Runtime.Application.Abstractions.Providers;
using Sentinel.Runtime.Application.Incidents.InProcess;
using Sentinel.Runtime.Application.Options;

namespace Sentinel.Runtime.Tests;

/// <summary>
/// Runtime's application layer over in-memory stores: the real command executor, detection,
/// workflow steps, processes and control path, with the single writer, workflows and Executor faked at the ports.
/// </summary>
internal sealed class InMemoryRuntime : IDisposable
{
    public static readonly TenantId Tenant = TenantId.From("acme");
    public static readonly EnvironmentId Environment = EnvironmentId.From("reference");

    public InMemoryRuntime(Action<IServiceCollection>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddSingleton<TimeProvider>(Time);
        services.AddSingleton(Store);
        services.AddSingleton<IIncidentStore>(Store);
        services.AddSingleton<ICorrelationStore>(Correlation);
        services.AddSingleton<IOutbox>(Outbox);
        services.AddSingleton<IUnitOfWork, NoUnitOfWork>();
        services.AddSingleton<IIncidentWorkflows>(Workflows);
        services.AddSingleton<IPolicyEvaluator>(Policy);
        services.AddSingleton<IObservationQueries>(Queries);
        services.AddSingleton<IServiceCatalog, InMemoryCatalog>();
        services.AddSingleton<IncidentLocks>();
        services.AddSingleton<IIncidentOwner, LocalIncidentOwner>();
        services.AddSingleton<IIncidentMemoryStore>(Memory);

        // Runtime's own application module: the processes, tasks and services exactly as the host registers them.
        new ApplicationDependencyModule().RegisterServices(services, new ConfigurationBuilder().Build());
        services.AddOptions<RuntimeOptions>().Configure(Configure);
        configure?.Invoke(services);
        Services = services.BuildServiceProvider();
    }

    public FakeTimeProvider Time { get; } = new(IncidentScenario.Start.AddMinutes(5));

    public InMemoryIncidentStore Store { get; } = new();

    public InMemoryCorrelationStore Correlation { get; } = new();

    public InMemoryIncidentMemoryStore Memory { get; } = new();

    public ConcurrentOutbox Outbox { get; } = new();

    public RecordingWorkflows Workflows { get; } = new();

    public StubPolicy Policy { get; } = new();

    public StubQueries Queries { get; } = new();

    public ServiceProvider Services { get; }

    public IIncidentOwner Owner => Services.GetRequiredService<IIncidentOwner>();

    public static void Configure(RuntimeOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        options.Detection.Add(new DetectionRuleOptions { Service = "checkout-service", Metric = "slo_burn_rate", DetectionThreshold = 2, OnsetThreshold = 1, RequiredSamples = 2 });
        options.Dependencies["checkout-service"] = ["order-service"];
        options.Recovery["checkout-service"] = [new RecoveryCriterionOptions { Metric = "slo_burn_rate", Comparison = ThresholdComparison.LessThan, Threshold = 1 }];
        options.Owners["checkout-service"] = "payments";
        options.BatchServices.Add("reporting-worker");
    }

    public async Task<T> InScopeAsync<T>(Func<IServiceProvider, Task<T>> action)
    {
        ArgumentNullException.ThrowIfNull(action);

        await using AsyncServiceScope scope = Services.CreateAsyncScope();
        return await action(scope.ServiceProvider);
    }

    /// <summary>A burn sample; <paramref name="run"/> keeps observation IDs, and so incident IDs, unique per test run.</summary>
    public static MetricObservation Burn(int secondsFromStart, double value, string run = "") => new()
    {
        ObservationId = ObservationId.From($"prometheus:slo_burn_rate:checkout-service:{run}{secondsFromStart}"),
        TenantId = Tenant,
        EnvironmentId = Environment,
        ServiceId = IncidentScenario.Checkout,
        SourceProvider = "prometheus",
        SourceResource = "slo:checkout_success:burn_rate1m",
        EventTime = IncidentScenario.Start.AddSeconds(secondsFromStart),
        IngestTime = IncidentScenario.Start.AddSeconds(secondsFromStart + 10),
        MetricName = "slo_burn_rate",
        Value = value
    };

    public void Dispose() => Services.Dispose();

    private sealed class NoUnitOfWork : IUnitOfWork
    {
        public Task CommitAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class InMemoryCatalog(ICorrelationStore correlation, TimeProvider time) : IServiceCatalog
    {
        public Task<ServiceContext> GetAsync(TenantId tenantId, EnvironmentId environmentId, ServiceId serviceId, CancellationToken cancellationToken) =>
            Task.FromResult(new ServiceContext(serviceId, null, [], []));

        public Task ObserveEdgeAsync(TenantId tenantId, EnvironmentId environmentId, ServiceId caller, ServiceId callee, CancellationToken cancellationToken) =>
            correlation.SaveEdgeAsync(tenantId, environmentId, caller, callee, time.GetUtcNow(), cancellationToken);
    }
}

/// <summary>An event store with the production store's optimistic append and a rebuildable projection.</summary>
internal sealed class InMemoryIncidentStore : IIncidentStore
{
    private readonly object _gate = new();
    private readonly Dictionary<IncidentId, List<StoredEvent>> _streams = [];
    private readonly Dictionary<IncidentId, (IncidentDetail Detail, bool IsOpen)> _projections = [];

    public int AppendCount { get; private set; }

    public Task<IReadOnlyList<StoredEvent>> ReadStreamAsync(IncidentId incidentId, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return Task.FromResult<IReadOnlyList<StoredEvent>>(_streams.TryGetValue(incidentId, out List<StoredEvent>? stream) ? [.. stream] : []);
        }
    }

    public Task AppendAsync(Incident incident, long expectedVersion, IReadOnlyList<StoredEvent> events, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(incident);
        ArgumentNullException.ThrowIfNull(events);

        lock (_gate)
        {
            if (!_streams.TryGetValue(incident.Id, out List<StoredEvent>? stream))
            {
                stream = [];
                _streams[incident.Id] = stream;
            }

            if (stream.Count != expectedVersion)
            {
                throw new IncidentConcurrencyException(incident.Id, expectedVersion);
            }

            stream.AddRange(events);
            AppendCount++;
        }

        return Task.CompletedTask;
    }

    public Task SaveProjectionAsync(IncidentDetail detail, bool isOpen, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(detail);

        lock (_gate)
        {
            _projections[detail.Summary.IncidentId] = (detail, isOpen);
        }

        return Task.CompletedTask;
    }

    /// <summary>Deletes every projection, as an operator might before a rebuild.</summary>
    public void DeleteProjections()
    {
        lock (_gate)
        {
            _projections.Clear();
        }
    }

    public Task<IncidentId?> FindOpenAsync(TenantId tenantId, EnvironmentId environmentId, ServiceId serviceId, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return Task.FromResult<IncidentId?>(_projections.Values
                .Where(entry => entry.IsOpen && entry.Detail.Summary.AffectedServiceId == serviceId)
                .Select(entry => (IncidentId?)entry.Detail.Summary.IncidentId)
                .FirstOrDefault());
        }
    }

    public Task<IReadOnlyList<IncidentContext>> CorrelationCandidatesAsync(TenantId tenantId, EnvironmentId environmentId, DateTimeOffset resolvedSince, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return Task.FromResult<IReadOnlyList<IncidentContext>>([.. _projections.Values
                .Where(entry => entry.IsOpen || entry.Detail.Summary.UpdatedAt >= resolvedSince)
                .Select(entry => new IncidentContext(
                    entry.Detail.Summary.IncidentId,
                    entry.Detail.Summary.AffectedServiceId,
                    entry.Detail.AffectedServices,
                    entry.Detail.Summary.SymptomOnset ?? entry.Detail.Summary.OpenedAt,
                    entry.IsOpen,
                    entry.IsOpen ? null : entry.Detail.Summary.UpdatedAt))]);
        }
    }

    public Task<long?> ProjectionVersionAsync(IncidentId incidentId, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return Task.FromResult<long?>(_projections.TryGetValue(incidentId, out (IncidentDetail Detail, bool IsOpen) entry) ? entry.Detail.Summary.StreamVersion : null);
        }
    }

    public Task<IReadOnlyList<IncidentSummary>> ListAsync(int limit, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return Task.FromResult<IReadOnlyList<IncidentSummary>>([.. _projections.Values.Select(entry => entry.Detail.Summary).OrderByDescending(summary => summary.OpenedAt).Take(limit)]);
        }
    }

    public Task<IncidentDetail?> GetDetailAsync(IncidentId incidentId, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return Task.FromResult(_projections.TryGetValue(incidentId, out (IncidentDetail Detail, bool IsOpen) entry) ? entry.Detail : null);
        }
    }

    public Task<IReadOnlyList<IncidentId>> AllIncidentIdsAsync(CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return Task.FromResult<IReadOnlyList<IncidentId>>([.. _streams.Keys]);
        }
    }
}

/// <summary>Incident memory with the production store's semantics: tenant-scoped retrieval, retention, legal hold and final deletion.</summary>
internal sealed class InMemoryIncidentMemoryStore : IIncidentMemoryStore
{
    private readonly ConcurrentDictionary<IncidentId, IncidentMemoryView> _records = new();

    public IReadOnlyCollection<IncidentMemoryView> Records => [.. _records.Values];

    public Task SaveAsync(IncidentMemoryView memory, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(memory);

        _records.AddOrUpdate(memory.IncidentId, memory, (_, existing) => existing.Retention.DeletedAt is null ? memory : existing);
        return Task.CompletedTask;
    }

    public Task<IncidentMemoryView?> GetAsync(IncidentId incidentId, CancellationToken cancellationToken) =>
        Task.FromResult(_records.TryGetValue(incidentId, out IncidentMemoryView? memory) ? memory : null);

    public Task<IReadOnlyList<IncidentMemoryView>> CandidatesAsync(TenantId tenantId, EnvironmentId? environmentId, IReadOnlyCollection<string> features, DateTimeOffset now, int limit, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<IncidentMemoryView>>([.. _records.Values
            .Where(memory => memory.TenantId == tenantId
                && (environmentId is null || memory.EnvironmentId == environmentId)
                && memory.Retention.DeletedAt is null
                && (memory.Retention.RetainUntil > now || memory.Retention.LegalHold)
                && memory.Features.Any(features.Contains))
            .OrderByDescending(memory => memory.RecordedAt)
            .Take(limit)]);

    public Task<IReadOnlyList<IncidentMemoryView>> ExpiredAsync(DateTimeOffset now, int limit, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<IncidentMemoryView>>([.. _records.Values
            .Where(memory => memory.Retention.DeletedAt is null && !memory.Retention.LegalHold && memory.Retention.RetainUntil <= now)
            .OrderBy(memory => memory.Retention.RetainUntil)
            .Take(limit)]);

    public Task DeleteAsync(IncidentMemoryView tombstone, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tombstone);

        _records[tombstone.IncidentId] = tombstone;
        return Task.CompletedTask;
    }
}

internal sealed class InMemoryCorrelationStore : ICorrelationStore
{
    private readonly ConcurrentDictionary<string, SignalStreak> _streaks = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, LinkedChange> _changes = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<(ServiceId, ServiceId), DateTimeOffset> _edges = new();

    public Task<SignalStreak?> GetStreakAsync(TenantId tenantId, EnvironmentId environmentId, ServiceId serviceId, string metricName, CancellationToken cancellationToken) =>
        Task.FromResult(_streaks.TryGetValue($"{serviceId}/{metricName}", out SignalStreak? streak) ? streak : null);

    public Task SaveStreakAsync(SignalStreak streak, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(streak);

        _streaks[$"{streak.ServiceId}/{streak.MetricName}"] = streak;
        return Task.CompletedTask;
    }

    public Task<LinkedChange> SaveChangeAsync(TenantId tenantId, EnvironmentId environmentId, LinkedChange change, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(change);

        LinkedChange saved = _changes.AddOrUpdate(change.DeploymentId, change, (_, existing) => existing with
        {
            CompletedAt = change.CompletedAt ?? existing.CompletedAt,
            Failed = existing.Failed || change.Failed,
            ObservationIds = [.. existing.ObservationIds.Concat(change.ObservationIds).Distinct()]
        });
        return Task.FromResult(saved);
    }

    public Task<IReadOnlyList<LinkedChange>> GetChangesAsync(TenantId tenantId, EnvironmentId environmentId, IReadOnlyCollection<ServiceId> services, DateTimeOffset startedFrom, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<LinkedChange>>([.. _changes.Values
            .Where(change => (services.Count == 0 || services.Contains(change.ServiceId)) && change.StartedAt >= startedFrom)
            .OrderBy(change => change.StartedAt)]);

    public Task<LinkedChange?> GetChangeAsync(TenantId tenantId, EnvironmentId environmentId, string deploymentId, CancellationToken cancellationToken) =>
        Task.FromResult(_changes.TryGetValue(deploymentId, out LinkedChange? change) ? change : null);

    public Task SaveEdgeAsync(TenantId tenantId, EnvironmentId environmentId, ServiceId caller, ServiceId callee, DateTimeOffset seenAt, CancellationToken cancellationToken)
    {
        _edges[(caller, callee)] = seenAt;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<(ServiceId Caller, ServiceId Callee)>> GetEdgesAsync(TenantId tenantId, EnvironmentId environmentId, DateTimeOffset seenSince, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<(ServiceId Caller, ServiceId Callee)>>([.. _edges.Keys]);
}

internal sealed class ConcurrentOutbox : IOutbox
{
    private readonly ConcurrentQueue<object> _messages = new();

    public IReadOnlyList<object> Messages => [.. _messages];

    public void Enqueue<TMessage>(TMessage message, MessageMetadata? metadata = null)
        where TMessage : class => _messages.Enqueue(message);

    public IEnumerable<TMessage> Of<TMessage>() => _messages.OfType<TMessage>();
}

internal sealed class RecordingWorkflows : IIncidentWorkflows
{
    public ConcurrentQueue<(IncidentId Incident, int Cycle)> Started { get; } = new();

    public ConcurrentQueue<(IncidentId Incident, IncidentSignalKind Kind)> Signals { get; } = new();

    public Task EnsureStartedAsync(IncidentSnapshot incident, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(incident);

        if (!Started.Contains((incident.IncidentId, incident.Cycle)))
        {
            Started.Enqueue((incident.IncidentId, incident.Cycle));
        }

        return Task.CompletedTask;
    }

    public Task SignalAsync(IncidentSnapshot incident, IncidentSignal signal, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(incident);
        ArgumentNullException.ThrowIfNull(signal);

        Signals.Enqueue((incident.IncidentId, signal.Kind));
        return Task.CompletedTask;
    }
}

internal sealed class StubPolicy : IPolicyEvaluator
{
    public int Calls { get; private set; }

    public PolicyEvaluationRequest? Last { get; private set; }

    public Task<PolicyEvaluation> EvaluateAsync(PolicyEvaluationRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Calls++;
        Last = request;
        return Task.FromResult(request.Plan.Actions[0] is PauseBatchAction
            ? new PolicyEvaluation(PolicyDecision.SuggestionOnly, "policy-test", ["pause-batch is suggestion-only"], IncidentScenario.Start)
            : new PolicyEvaluation(PolicyDecision.ApprovalRequired, "policy-test", ["rollback needs approval"], IncidentScenario.Start));
    }
}

/// <summary>Answers recovery queries with a healthy, complete signal.</summary>
internal sealed class StubQueries : IObservationQueries
{
    public double Value { get; set; } = 0.2;

    /// <summary>The value before the recovery window, the pre-action baseline; defaults to <see cref="Value"/>.</summary>
    public double? Before { get; set; }

    public DateTimeOffset? WindowFrom { get; set; }

    public Task<ObservationQueryResult> QueryAsync(ObservationQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        // Five samples spread across the window, the last inside its final third, whatever its length.
        TimeSpan span = query.Range.To - query.Range.From;
        double value = Before is { } before && WindowFrom is { } from && query.Range.To <= from ? before : Value;
        MetricObservation[] samples = [.. Enumerable.Range(0, 5).Select(index => InMemoryRuntime.Burn(0, value) with
        {
            ObservationId = ObservationId.From($"prometheus:slo_burn_rate:checkout-service:recovery:{index}"),
            EventTime = query.Range.From + span * index / 5
        })];
        return Task.FromResult(new ObservationQueryResult(samples, new QueryMetadata
        {
            ObservedThrough = query.Range.To,
            Freshness = TimeSpan.Zero,
            Completeness = QueryCompleteness.Complete,
            Truncated = false,
            ProviderHealth = [],
            RedactionPolicyVersion = "test"
        }));
    }
}
