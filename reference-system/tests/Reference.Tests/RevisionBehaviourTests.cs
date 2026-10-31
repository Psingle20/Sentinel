using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Internal;
using Microsoft.Extensions.Time.Testing;
using Reference.Common.Control;
using Reference.Common.Revisions;
using Shouldly;
using Reference.Checkout.Orders;
using Reference.Checkout.Revisions;
using Reference.Orders.Revisions;
using Reference.Orders.Storage;
using Reference.RedisExporter.Sampling;

namespace Reference.Tests;

public sealed class RevisionBehaviourTests
{
    [Fact]
    public void Healthy_order_revisions_keep_a_flat_query_latency()
    {
        OrderStore.QueryLatency(OrderRevisions.Baseline, TimeSpan.FromMinutes(30)).ShouldBe(OrderStore.BaseQueryLatency);
        OrderStore.QueryLatency("v1.7.1", TimeSpan.FromMinutes(30)).ShouldBe(OrderStore.BaseQueryLatency);
    }

    [Fact]
    public void The_slow_query_crosses_checkouts_timeout_only_after_the_S3_confounder_serves()
    {
        // S3: v1.8.0 serves from 20 s; checkout v2.4.3 serves from 140 s.
        TimeSpan atConfounderServing = OrderStore.QueryLatency(OrderRevisions.SlowQuery, TimeSpan.FromSeconds(140 - 20));
        TimeSpan threeMinutesIn = OrderStore.QueryLatency(OrderRevisions.SlowQuery, TimeSpan.FromSeconds(180 - 20));

        atConfounderServing.ShouldBeLessThan(OrderServiceClient.AttemptTimeout);
        threeMinutesIn.ShouldBeGreaterThan(OrderServiceClient.AttemptTimeout);
        OrderStore.QueryLatency(OrderRevisions.SlowQuery, TimeSpan.FromHours(1)).ShouldBe(OrderStore.MaxQueryLatency);
    }

    [Fact]
    public void The_cold_cache_misses_two_lookups_in_five_and_each_miss_outlasts_checkouts_attempt()
    {
        // S10: v1.8.1 is slow from its first request on a miss and fast on a hit, so most checkouts still succeed on a retry.
        SlowOrderLookup.Latency(OrderRevisions.ColdCache, TimeSpan.Zero, draw: 0.1).ShouldBe(SlowOrderLookup.CacheMissLatency);
        SlowOrderLookup.Latency(OrderRevisions.ColdCache, TimeSpan.Zero, draw: 0.6).ShouldBe(OrderStore.BaseQueryLatency);
        (SlowOrderLookup.CacheMissLatency * 0.9).ShouldBeGreaterThan(OrderServiceClient.AttemptTimeout, "a miss times out even with jitter");
        SlowOrderLookup.Latency(OrderRevisions.Baseline, TimeSpan.Zero, draw: 0.1).ShouldBe(OrderStore.BaseQueryLatency, "only v1.8.1 has the cold cache");
        SlowOrderLookup.Latency(OrderRevisions.SlowQuery, TimeSpan.FromSeconds(10), draw: 0.1).ShouldBe(OrderStore.QueryLatency(OrderRevisions.SlowQuery, TimeSpan.FromSeconds(10)));
    }

    [Fact]
    public async Task A_revision_switch_runs_handlers_once_and_ignores_unknown_revisions()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 10, 28, 9, 0, 0, TimeSpan.Zero));
        var state = new RevisionState(CheckoutRevisions.ServiceName, CheckoutRevisions.Baseline, CheckoutRevisions.All, time);
        var changes = new List<RevisionChange>();
        state.OnChanged(change =>
        {
            changes.Add(change);
            return Task.CompletedTask;
        });

        time.Advance(TimeSpan.FromMinutes(1));
        (await state.SwitchAsync(CheckoutRevisions.ConnectionLeak)).ShouldBeTrue();
        (await state.SwitchAsync(CheckoutRevisions.ConnectionLeak)).ShouldBeTrue();
        (await state.SwitchAsync("v9.9.9")).ShouldBeFalse();

        changes.ShouldHaveSingleItem().ShouldBe(new RevisionChange(CheckoutRevisions.Baseline, CheckoutRevisions.ConnectionLeak, time.GetUtcNow()));
        state.Current.ShouldBe(CheckoutRevisions.ConnectionLeak);
        state.Uptime.ShouldBe(TimeSpan.Zero);
    }

    [Fact]
    public void Client_list_lines_are_counted_per_client_name()
    {
        const string clientList = """
            id=3 addr=172.17.0.1:50000 laddr=172.17.0.2:6379 fd=8 name=checkout-service age=4 idle=0
            id=4 addr=172.17.0.1:50002 laddr=172.17.0.2:6379 fd=9 name=checkout-service age=4 idle=0
            id=5 addr=172.17.0.1:50004 laddr=172.17.0.2:6379 fd=10 name=reporting-worker age=1 idle=1
            id=6 addr=172.17.0.1:50006 laddr=172.17.0.2:6379 fd=11 name= age=9 idle=9
            """;

        Dictionary<string, int> counts = RedisClientSampler.CountByName(clientList);

        counts.ShouldBe(new Dictionary<string, int> { ["checkout-service"] = 2, ["reporting-worker"] = 1, ["unnamed"] = 1 }, ignoreOrder: true);
    }

    [Theory]
    [InlineData("Production", true, "token", "token", false)]
    [InlineData("Development", false, "token", "token", false)]
    [InlineData("Development", true, "token", "wrong", false)]
    [InlineData("Development", true, "token", null, false)]
    [InlineData("Development", true, "token", "token", true)]
    [InlineData("Test", true, "token", "token", true)]
    public void Failure_controls_exist_only_in_development_or_test_and_require_the_token(
        string environmentName, bool enabled, string configuredToken, string? presentedToken, bool authorized)
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Reference:FailureControls:Enabled"] = enabled.ToString(),
                ["Reference:ControlToken"] = configuredToken
            })
            .Build();
        var environment = new HostingEnvironment { EnvironmentName = environmentName };
        var context = new DefaultHttpContext();
        if (presentedToken is not null)
        {
            context.Request.Headers[FailureControl.TokenHeader] = presentedToken;
        }

        FailureControl.From(configuration, environment).IsAuthorized(context.Request).ShouldBe(authorized);
    }

    [Fact]
    public void Enabled_failure_controls_without_a_token_fail_at_startup()
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Reference:FailureControls:Enabled"] = "true" })
            .Build();

        Should.Throw<InvalidOperationException>(() => FailureControl.From(configuration, new HostingEnvironment { EnvironmentName = Environments.Development }));
    }
}
