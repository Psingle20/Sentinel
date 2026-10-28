using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Reference.Common.Control;
using Reference.DeployController;
using Shouldly;

namespace Reference.Tests;

public sealed class DeploymentLedgerTests : IDisposable
{
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 28, 9, 0, 0, TimeSpan.Zero));
    private readonly FakeServices _services = new();
    private readonly ServiceProvider _provider;
    private readonly DeploymentLedger _ledger;

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
        collection.AddSingleton(configuration);
        collection.AddHttpClient<RevisionClient>().ConfigurePrimaryHttpMessageHandler(() => _services);
        _provider = collection.BuildServiceProvider();
        _ledger = new DeploymentLedger(
            new DeployControllerOptionsAccessor(options),
            _provider.GetRequiredService<IServiceScopeFactory>(),
            _time,
            new StoppedNeverLifetime(),
            NullLogger<DeploymentLedger>.Instance);
    }

    public void Dispose()
    {
        _provider.Dispose();
        _services.Dispose();
    }

    [Fact]
    public async Task A_deployment_switches_the_revision_only_when_its_rollout_completes()
    {
        DeployResult result = await _ledger.StartAsync("checkout-service", new DeployRequest("v2.4.1", Actor: "test"), null, CancellationToken.None);

        result.Outcome.ShouldBe(DeployOutcome.Accepted);
        result.Deployment!.PreviousVersion.ShouldBe("v2.4.0");
        _services.Revisions["checkout-service"].ShouldBe("v2.4.0");

        // The rollout registers its delay on a background task; advance in steps until it completes.
        for (int second = 0; second < 19; second++)
        {
            await Task.Delay(10);
            _time.Advance(TimeSpan.FromSeconds(1));
        }

        _ledger.Find(result.Deployment.DeploymentId)!.Status.ShouldBe(DeploymentStatus.InProgress);
        _time.Advance(TimeSpan.FromSeconds(1));
        await EventuallyAsync(() => _ledger.Find(result.Deployment.DeploymentId)!.Status == DeploymentStatus.Completed);

        _services.Revisions["checkout-service"].ShouldBe("v2.4.1");
        _ledger.Find(result.Deployment.DeploymentId)!.CompletedAt.ShouldBe(result.Deployment.StartedAt.AddSeconds(20));
    }

    [Fact]
    public async Task Repeating_a_request_with_the_same_idempotency_key_returns_the_original_deployment()
    {
        DeployResult first = await _ledger.StartAsync("checkout-service", new DeployRequest("v2.4.1"), "rollback-1", CancellationToken.None);
        DeployResult second = await _ledger.StartAsync("checkout-service", new DeployRequest("v2.4.1"), "rollback-1", CancellationToken.None);

        second.Outcome.ShouldBe(DeployOutcome.Replayed);
        second.Deployment!.DeploymentId.ShouldBe(first.Deployment!.DeploymentId);
        _ledger.Query(null, null, "checkout-service").ShouldHaveSingleItem();
    }

    [Fact]
    public async Task A_second_rollout_to_the_same_service_conflicts_and_unknown_revisions_are_refused()
    {
        await _ledger.StartAsync("checkout-service", new DeployRequest("v2.4.1"), null, CancellationToken.None);

        (await _ledger.StartAsync("checkout-service", new DeployRequest("v2.4.0"), null, CancellationToken.None)).Outcome.ShouldBe(DeployOutcome.Conflict);
        (await _ledger.StartAsync("order-service", new DeployRequest("v9.0.0"), null, CancellationToken.None)).Outcome.ShouldBe(DeployOutcome.UnknownRevision);
        (await _ledger.StartAsync("billing-service", new DeployRequest("v1.0.0"), null, CancellationToken.None)).Outcome.ShouldBe(DeployOutcome.UnknownService);
    }

    [Fact]
    public async Task A_configuration_change_is_recorded_immediately_without_switching_revisions()
    {
        DeployResult result = await _ledger.StartAsync(
            "order-service",
            new DeployRequest("ignored", DeploymentKind.ConfigChange, ConfigurationDiff: "log level"),
            null,
            CancellationToken.None);

        result.Deployment!.Status.ShouldBe(DeploymentStatus.Completed);
        result.Deployment.Version.ShouldBe("v1.7.0");
        _services.Switches.ShouldBe(0);
    }

    [Fact]
    public async Task Reset_returns_changed_services_to_baseline_and_is_idempotent()
    {
        _services.Revisions["order-service"] = "v1.8.0";

        IReadOnlyList<DeploymentRecord> first = await _ledger.ResetAsync(CancellationToken.None);
        IReadOnlyList<DeploymentRecord> second = await _ledger.ResetAsync(CancellationToken.None);

        first.ShouldHaveSingleItem().Kind.ShouldBe(DeploymentKind.Reset);
        first[0].PreviousVersion.ShouldBe("v1.8.0");
        _services.Revisions["order-service"].ShouldBe("v1.7.0");
        second.ShouldBeEmpty();
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
