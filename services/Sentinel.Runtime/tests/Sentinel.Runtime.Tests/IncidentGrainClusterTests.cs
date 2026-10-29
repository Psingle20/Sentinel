using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Integration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Orleans.Runtime;
using Orleans.TestingHost;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Runtime.Application;
using Sentinel.Runtime.Application.Incidents;
using Sentinel.Runtime.Domain.Incidents;
using Sentinel.Runtime.Infrastructure;
using Sentinel.Runtime.Infrastructure.Grains;
using Shouldly;
using Sentinel.Runtime.Application.Abstractions.Runtime;

namespace Sentinel.Runtime.Tests;

/// <summary>
/// Phase 03 actor tests on a real in-process Orleans cluster of two silos sharing one event store:
/// serialized commands, deactivation, silo loss and partitioned correlation (ADR-004).
/// </summary>
[Trait("Category", "Integration")]
public sealed class IncidentGrainClusterTests : IAsyncLifetime
{
    private readonly InMemoryIncidentStore _store = new();
    private readonly InMemoryCorrelationStore _correlation = new();
    private readonly ConcurrentOutbox _outbox = new();
    private readonly RecordingWorkflows _workflows = new();
    private readonly FakeTimeProvider _time = new(IncidentScenario.Start.AddMinutes(5));
    private InProcessTestCluster _cluster = null!;

    public async Task InitializeAsync()
    {
        var builder = new InProcessTestClusterBuilder(2);
        builder.ConfigureSilo((_, silo) =>
        {
            IServiceCollection services = silo.Services;
            services.AddRuntimeGrainSerialization();
            services.AddSingleton<TimeProvider>(_time);
            services.AddSingleton<IIncidentStore>(_store);
            services.AddSingleton<ICorrelationStore>(_correlation);
            services.AddSingleton<IOutbox>(_outbox);
            services.AddSingleton<IUnitOfWork, CommitNothing>();
            services.AddSingleton<IIncidentWorkflows>(_workflows);
            services.AddSingleton<IIncidentOwner, GrainIncidentOwner>();
            services.AddSingleton<IServiceCatalog, GrainServiceCatalog>();
            services.AddSingleton<ServiceTopologyCache>();
            services.AddSingleton<IIncidentMemoryStore, InMemoryIncidentMemoryStore>();
            services.AddScoped<Sentinel.Runtime.Application.Memory.IncidentMemoryRecorder>();
            services.AddScoped<IncidentCommandExecutor>();
            services.AddScoped<DetectionService>();
            services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
            services.AddOptions<RuntimeOptions>().Configure(InMemoryRuntime.Configure);
        });
        builder.ConfigureClient(client => client.Services.AddRuntimeGrainSerialization());
        _cluster = builder.Build();
        await _cluster.DeployAsync();
    }

    public async Task DisposeAsync() => await _cluster.DisposeAsync();

    [Fact]
    public async Task Concurrent_callers_cannot_interleave_commands_for_one_incident()
    {
        IncidentId incidentId = await OpenAsync();
        IIncidentGrain grain = _cluster.Client.GetGrain<IIncidentGrain>(incidentId.Value);

        CommandOutcome[] outcomes = await Task.WhenAll(Enumerable.Range(0, 30).Select(index => grain.Send(
            new LinkSignalCommand(ServiceId.From($"service-{index}"), "latency_p95", index, [ObservationId.From($"o-{index}")], 0.5, ["test"]))));

        outcomes.ShouldAllBe(outcome => outcome.Reply != null && outcome.Reply.Changed);
        IReadOnlyList<StoredEvent> stream = await _store.ReadStreamAsync(incidentId, CancellationToken.None);
        stream.Select(stored => stored.Version).ShouldBe(Enumerable.Range(1, stream.Count).Select(version => (long)version));
    }

    [Fact]
    public async Task A_deactivated_grain_rehydrates_exactly_from_the_stream()
    {
        IncidentId incidentId = await OpenAsync();
        IIncidentGrain grain = _cluster.Client.GetGrain<IIncidentGrain>(incidentId.Value);
        IncidentSnapshot before = (await grain.Get())!;

        await _cluster.DeactivateAsync(grain);
        await _cluster.WaitForDeactivationAsync(grain);
        IncidentSnapshot after = (await grain.Get())!;

        after.ShouldBeEquivalentTo(before);
        (await grain.Send(new EscalateCommand("handed to responders"))).Reply!.Snapshot.State.ShouldBe(IncidentState.Escalated);
    }

    [Fact]
    public async Task Losing_the_silo_that_hosts_an_incident_loses_no_accepted_state()
    {
        IncidentId incidentId = await OpenAsync();
        IIncidentGrain grain = _cluster.Client.GetGrain<IIncidentGrain>(incidentId.Value);
        await grain.Send(new EscalateCommand("handed to responders"));
        long version = (await grain.Get())!.Version;

        _cluster.TryGetGrainContext(grain.GetGrainId(), out IGrainContext? context).ShouldBeTrue();
        InProcessSiloHandle host = _cluster.Silos.Single(silo => silo.SiloAddress.Equals(context!.Address.SiloAddress));
        await _cluster.KillSiloAsync(host);
        await _cluster.WaitForLivenessToStabilizeAsync();

        IncidentSnapshot recovered = await RetryAsync(async () => (await grain.Get())!);
        recovered.Version.ShouldBe(version);
        recovered.State.ShouldBe(IncidentState.Escalated);
        (await RetryAsync(() => grain.Send(new ResolveClearedCommand("cleared")))).Reply!.Snapshot.State.ShouldBe(IncidentState.Resolved);
    }

    [Fact]
    public async Task A_burst_of_samples_through_one_partition_opens_exactly_one_incident()
    {
        ICorrelationGrain partition = _cluster.Client.GetGrain<ICorrelationGrain>("acme/reference");

        await Task.WhenAll(Enumerable.Range(0, 12).Select(index => partition.Observe(InMemoryRuntime.Burn(index * 5, 4 + index))));

        (await _store.ListAsync(10, CancellationToken.None)).ShouldHaveSingleItem();
        _workflows.Started.ShouldHaveSingleItem();
    }

    private async Task<IncidentId> OpenAsync()
    {
        var incidentId = IncidentId.New();
        CommandOutcome opened = await _cluster.Client.GetGrain<IIncidentGrain>(incidentId.Value).Send(new OpenIncidentCommand(
            InMemoryRuntime.Tenant,
            InMemoryRuntime.Environment,
            IncidentScenario.Checkout,
            "checkout-service slo_burn_rate at 12.0",
            IncidentSeverity.High,
            IncidentScenario.Start,
            [ObservationId.From("prometheus:slo_burn_rate:checkout-service:1")],
            []));
        opened.Reply.ShouldNotBeNull();
        return incidentId;
    }


    private static async Task<T> RetryAsync<T>(Func<Task<T>> call)
    {
        int attempt = 0;
        while (true)
        {
            attempt++;
            try
            {
                return await call();
            }
            catch (Exception exception) when (attempt < 10 && exception is SiloUnavailableException or OrleansMessageRejectionException or TimeoutException)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(500));
            }
        }
    }

    private sealed class CommitNothing : IUnitOfWork
    {
        public Task CommitAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
