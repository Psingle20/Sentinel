using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Reference.Common.Redis;
using Reference.Common.Revisions;
using Shouldly;
using Testcontainers.Redis;
using Reference.Checkout.Redis;
using Reference.Checkout.Revisions;
using Reference.ReportingWorker.Batch;
using Reference.ReportingWorker.Revisions;
using Reference.RedisExporter.Sampling;

namespace Reference.Tests;

[Collection(RedisCollection.Name)]
public sealed class RedisBehaviourTests(SmallRedis redis)
{
    [Fact]
    public async Task Checkout_v2_4_1_leaks_a_connection_every_second_session_and_a_rollback_releases_them()
    {
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var revision = new RevisionState(CheckoutRevisions.ServiceName, CheckoutRevisions.ConnectionLeak, CheckoutRevisions.All, time);
        await using var checkout = new CheckoutRedis(redis.Endpoint, revision, time);

        for (int session = 0; session < 10; session++)
        {
            await checkout.TouchSessionAsync($"cart-{session}", revision.Current, CancellationToken.None);
        }

        checkout.LeakedConnections.ShouldBe(5);
        await redis.EventuallyAsync(CheckoutRevisions.ServiceName, count => count == checkout.LeakedConnections + checkout.PoolConnections);

        await revision.SwitchAsync(CheckoutRevisions.Baseline);

        checkout.LeakedConnections.ShouldBe(0);
        await redis.EventuallyAsync(CheckoutRevisions.ServiceName, count => count == 0);
    }

    [Fact]
    public async Task Healthy_checkout_keeps_a_flat_pool_and_fails_when_a_saturated_redis_refuses_replacements()
    {
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var revision = new RevisionState(CheckoutRevisions.ServiceName, CheckoutRevisions.Baseline, CheckoutRevisions.All, time);
        await using var checkout = new CheckoutRedis(redis.Endpoint, revision, time);
        await using var batch = new ReportingBatch(redis.Endpoint, TimeProvider.System, NullLogger<ReportingBatch>.Instance);

        for (int session = 0; session < 8; session++)
        {
            await checkout.TouchSessionAsync($"cart-{session}", revision.Current, CancellationToken.None);
        }

        checkout.PoolConnections.ShouldBe(4);
        checkout.LeakedConnections.ShouldBe(0);

        batch.Start(new StartBatchRequest(TargetConnections: SmallRedis.MaxClients, ConnectionsPerSecond: 200));
        // Redis is now full, so wait on the batch itself rather than opening a probe connection.
        for (int attempt = 0; attempt < 100 && batch.Status().LastError is null; attempt++)
        {
            await Task.Delay(50);
        }

        batch.Status().LastError.ShouldNotBeNull();

        // Housekeeping evicts pooled connections after 5 s; the batch takes each freed slot, so the refill is refused.
        time.Advance(TimeSpan.FromSeconds(6));
        await Task.Delay(500);
        checkout.PoolConnections.ShouldBe(0);
        await Should.ThrowAsync<RedisUnavailableException>(() => checkout.TouchSessionAsync("cart-late", revision.Current, CancellationToken.None));

        await batch.StopAsync();
        await redis.EventuallyAsync(ReportingRevisions.ServiceName, count => count == 0);
        await checkout.TouchSessionAsync("cart-recovered", revision.Current, CancellationToken.None);
    }

    [Fact]
    public async Task Stopping_a_batch_twice_is_safe_and_closes_every_connection()
    {
        await using var batch = new ReportingBatch(redis.Endpoint, TimeProvider.System, NullLogger<ReportingBatch>.Instance);

        batch.Start(new StartBatchRequest(TargetConnections: 5, ConnectionsPerSecond: 100));
        await redis.EventuallyAsync(ReportingRevisions.ServiceName, count => count == 5);

        (await batch.StopAsync()).Active.ShouldBeFalse();
        (await batch.StopAsync()).Connections.ShouldBe(0);
        await redis.EventuallyAsync(ReportingRevisions.ServiceName, count => count == 0);
    }
}
