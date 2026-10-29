using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using CleanArchitecture.BuildingBlocks.Messaging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Time.Testing;
using Reference.Common.Control;
using Reference.Common.Revisions;
using Reference.DeployController.Api.Clients;
using Reference.DeployController.Api.Rollouts;
using Reference.DeployController.Application;
using Reference.DeployController.Application.Abstractions;
using Reference.DeployController.Application.BusinessProcesses.Deployments;
using Reference.DeployController.Application.Options;
using Reference.DeployController.Application.Services;
using Reference.DeployController.Domain.Revisions;
using Shouldly;

namespace Reference.Tests;

/// <summary>
/// The deploy controller's deployments as its application runs them, with the reference services'
/// revision endpoints played by a fake and the rollout timer on a fake clock.
/// </summary>
public sealed class DeploymentLedgerTests : IDisposable
{
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 28, 9, 0, 0, TimeSpan.Zero));
    private readonly FakeServices _services = new();
    private readonly ServiceProvider _provider;
    private readonly DeploymentFaults _faults;

    public DeploymentLedgerTests()
    {
        var options = new DeployControllerOptions
        {
            RolloutSeconds = 20,
            Services = new Dictionary<string, ManagedService>(StringComparer.Ordinal)
            {
                ["checkout-service"] = new() { Baseline = "v2.4.0", Revisions = ["v2.4.0", "v2.4.1"] },
                ["order-service"] = new() { Baseline = "v1.7.0", Revisions = ["v1.7.0", "v1.8.0"] }
            }
        };

        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["services:checkout-service:http:0"] = "http://checkout-service.test",
                ["services:order-service:http:0"] = "http://order-service.test",
                ["Reference:ControlToken"] = "test-control-token"
            })
            .Build();

        var collection = new ServiceCollection();
        collection.AddLogging();
        collection.AddSingleton(configuration);
        collection.AddSingleton(options);
        collection.AddSingleton<TimeProvider>(_time);
        collection.AddSingleton<IHostApplicationLifetime>(new StoppedNeverLifetime());
        new ApplicationDependencyModule().RegisterServices(collection, configuration);
        collection.AddHttpClient<IRevisionSwitch, RevisionClient>().ConfigurePrimaryHttpMessageHandler(() => _services);
        collection.AddSingleton<IRolloutScheduler, RolloutScheduler>();
        _provider = collection.BuildServiceProvider();
        _faults = _provider.GetRequiredService<DeploymentFaults>();
    }

    public void Dispose()
    {
        _provider.Dispose();
        _services.Dispose();
    }

    [Fact]
    public async Task A_deployment_switches_the_revision_only_when_its_rollout_completes()
    {
        DeployResult result = await StartAsync("checkout-service", "v2.4.1", actor: "test");

        result.Outcome.ShouldBe(DeployOutcome.Accepted);
        result.Deployment!.PreviousVersion.ShouldBe("v2.4.0");
        _services.Revisions["checkout-service"].ShouldBe("v2.4.0");

        // The rollout registers its delay on a background task; advance in steps until it completes.
        for (int second = 0; second < 19; second++)
        {
            await Task.Delay(10);
            _time.Advance(TimeSpan.FromSeconds(1));
        }

        Find(result.Deployment.DeploymentId)!.Status.ShouldBe(DeploymentStatus.InProgress);
        _time.Advance(TimeSpan.FromSeconds(1));
        await EventuallyAsync(() => Find(result.Deployment.DeploymentId)!.Status == DeploymentStatus.Completed);

        _services.Revisions["checkout-service"].ShouldBe("v2.4.1");
        Find(result.Deployment.DeploymentId)!.CompletedAt.ShouldBe(result.Deployment.StartedAt.AddSeconds(20));
    }

    [Fact]
    public async Task Repeating_a_request_with_the_same_idempotency_key_returns_the_original_deployment()
    {
        DeployResult first = await StartAsync("checkout-service", "v2.4.1", key: "rollback-1");
        DeployResult second = await StartAsync("checkout-service", "v2.4.1", key: "rollback-1");

        second.Outcome.ShouldBe(DeployOutcome.Replayed);
        second.Deployment!.DeploymentId.ShouldBe(first.Deployment!.DeploymentId);
        Query("checkout-service").ShouldHaveSingleItem();
    }

    [Fact]
    public async Task A_second_rollout_to_the_same_service_conflicts_and_unknown_revisions_are_refused()
    {
        await StartAsync("checkout-service", "v2.4.1");

        (await StartAsync("checkout-service", "v2.4.0")).Outcome.ShouldBe(DeployOutcome.Conflict);
        (await StartAsync("order-service", "v9.0.0")).Outcome.ShouldBe(DeployOutcome.UnknownRevision);
        (await StartAsync("billing-service", "v1.0.0")).Outcome.ShouldBe(DeployOutcome.UnknownService);
    }

    [Fact]
    public async Task A_configuration_change_is_recorded_immediately_without_switching_revisions()
    {
        DeployResult result = await StartAsync("order-service", "ignored", DeploymentKind.ConfigChange, diff: "log level");

        result.Deployment!.Status.ShouldBe(DeploymentStatus.Completed);
        result.Deployment.Version.ShouldBe("v1.7.0");
        _services.Switches.ShouldBe(0);
    }

    [Fact]
    public async Task Reset_returns_changed_services_to_baseline_and_is_idempotent()
    {
        _services.Revisions["order-service"] = "v1.8.0";

        IReadOnlyList<DeploymentRecord> first = await ResetAsync();
        IReadOnlyList<DeploymentRecord> second = await ResetAsync();

        first.ShouldHaveSingleItem().Kind.ShouldBe(DeploymentKind.Reset);
        first[0].PreviousVersion.ShouldBe("v1.8.0");
        _services.Revisions["order-service"].ShouldBe("v1.7.0");
        second.ShouldBeEmpty();
    }

    [Fact]
    public async Task An_injected_rollback_fault_fails_the_rollout_without_switching_and_reset_clears_it()
    {
        _services.Revisions["checkout-service"] = "v2.4.1";
        _faults.FailRollbacks = true;

        DeployResult result = await StartAsync("checkout-service", "v2.4.0", DeploymentKind.Rollback, "sentinel-executor", "rollback-faulted");
        await AdvanceRolloutAsync();

        await EventuallyAsync(() => Find(result.Deployment!.DeploymentId)!.Status == DeploymentStatus.Failed);
        Find(result.Deployment!.DeploymentId)!.Error.ShouldNotBeNull().ShouldContain("Injected fault");
        _services.Revisions["checkout-service"].ShouldBe("v2.4.1");

        await ResetAsync();
        _faults.FailRollbacks.ShouldBeFalse();
    }

    private async Task<DeployResult> StartAsync(string service, string version, DeploymentKind kind = DeploymentKind.Deploy, string? actor = null, string? key = null, string? diff = null)
    {
        await using AsyncServiceScope scope = _provider.CreateAsyncScope();
        return (await scope.ServiceProvider.GetRequiredService<IRequestDispatcher>().HandleRequestAsync(
            new StartDeploymentRequest(service, version, kind, actor, null, null, diff, key), CancellationToken.None)).Value;
    }

    private async Task<IReadOnlyList<DeploymentRecord>> ResetAsync()
    {
        await using AsyncServiceScope scope = _provider.CreateAsyncScope();
        return (await scope.ServiceProvider.GetRequiredService<IRequestDispatcher>().HandleRequestAsync(new ResetServicesRequest(), CancellationToken.None)).Value;
    }

    private DeploymentRecord? Find(string deploymentId) => _provider.GetRequiredService<LedgerState>().Use(ledger => ledger.Find(deploymentId));

    private IReadOnlyList<DeploymentRecord> Query(string service) => _provider.GetRequiredService<LedgerState>().Use(ledger => ledger.Query(null, null, service));

    private async Task AdvanceRolloutAsync()
    {
        for (int second = 0; second < 21; second++)
        {
            await Task.Delay(10);
            _time.Advance(TimeSpan.FromSeconds(1));
        }
    }

    private static async Task EventuallyAsync(Func<bool> condition)
    {
        for (int attempt = 0; attempt < 200 && !condition(); attempt++)
        {
            await Task.Delay(10);
        }

        condition().ShouldBeTrue();
    }

    /// <summary>Plays the reference services' revision endpoints.</summary>
    private sealed class FakeServices : HttpMessageHandler
    {
        private int _switches;

        public ConcurrentDictionary<string, string> Revisions { get; } = new(StringComparer.Ordinal)
        {
            ["checkout-service"] = "v2.4.0",
            ["order-service"] = "v1.7.0"
        };

        public int Switches => _switches;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string service = request.RequestUri!.Host.Replace(".test", string.Empty, StringComparison.Ordinal);
            if (request.Method == HttpMethod.Put)
            {
                request.Headers.GetValues(FailureControl.TokenHeader).ShouldHaveSingleItem().ShouldBe("test-control-token");
                SwitchRevisionRequest body = (await request.Content!.ReadFromJsonAsync<SwitchRevisionRequest>(cancellationToken))!;
                Revisions[service] = body.Revision;
                Interlocked.Increment(ref _switches);
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new RevisionView(service, Revisions[service], DateTimeOffset.UnixEpoch, []))
            };
        }
    }

    private sealed class StoppedNeverLifetime : IHostApplicationLifetime
    {
        public CancellationToken ApplicationStarted => CancellationToken.None;

        public CancellationToken ApplicationStopping => CancellationToken.None;

        public CancellationToken ApplicationStopped => CancellationToken.None;

        public void StopApplication()
        {
            // Never stops in these tests.
        }
    }
}
