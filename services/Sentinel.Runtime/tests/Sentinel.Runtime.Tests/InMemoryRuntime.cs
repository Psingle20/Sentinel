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
        services.AddSingleton<Sentinel.Runtime.Application.Abstractions.Security.IApprovalKeyResolver, TestDecisions.Keys>();

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
        options.QueueServices["queue-consumer"] = "orders";
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
