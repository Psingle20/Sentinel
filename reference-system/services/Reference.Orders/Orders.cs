using System.Diagnostics;
using System.Diagnostics.Metrics;
using Reference.Common;
using Reference.Common.Redis;
using Reference.Common.Revisions;
using Reference.Common.Telemetry;

namespace Reference.Orders;

public static class OrderRevisions
{
    public const string ServiceName = "order-service";
    public const string Baseline = "v1.7.0";

    /// <summary>S3's cause: an unindexed lookup whose latency grows with every second the revision serves.</summary>
    public const string SlowQuery = "v1.8.0";

    public static readonly IReadOnlyCollection<string> All = [Baseline, "v1.7.1", SlowQuery];
}

public sealed record OrderRequest(string CartId);

public sealed record OrderResult(string OrderId, string Revision);

public sealed class OrderTelemetry : IDisposable
{
    public const string SourceName = "Reference.Orders";

    private readonly Meter _meter = new(SourceName);

    public OrderTelemetry(OrderStore store)
    {
        ArgumentNullException.ThrowIfNull(store);

        Requests = _meter.CreateCounter<long>("reference.orders.requests", "{request}", "Order requests by outcome.");
        Duration = _meter.CreateHistogram("reference.orders.duration", "s", "Order handling duration.", tags: null, LatencyBuckets.Seconds);
        QueryDuration = _meter.CreateHistogram("reference.orders.query.duration", "s", "Order lookup query duration.", tags: null, LatencyBuckets.Seconds);
        _meter.CreateObservableGauge("reference.orders.redis.pool.connections", () => store.PoolConnections, "{connection}", "Open pooled Redis connections.");
    }

    public static ActivitySource Activities { get; } = new(SourceName);

    public Counter<long> Requests { get; }

    public Histogram<double> Duration { get; }

    public Histogram<double> QueryDuration { get; }

    public void Dispose() => _meter.Dispose();
}

/// <summary>Order persistence: a simulated database lookup plus an idempotency key in Redis.</summary>
public sealed class OrderStore : IAsyncDisposable
{
    public static readonly TimeSpan BaseQueryLatency = TimeSpan.FromMilliseconds(30);

    /// <summary>Added per second that v1.8.0 has been serving, so checkout's timeouts start about two minutes in.</summary>
    public static readonly TimeSpan SlowQueryGrowthPerSecond = TimeSpan.FromMilliseconds(4);

    public static readonly TimeSpan MaxQueryLatency = TimeSpan.FromSeconds(2);

    private readonly RedisConnectionPool _pool;
    private readonly RevisionState _revision;
    private readonly SeededJitter _jitter;
    private readonly TimeProvider _time;

    public OrderStore(RedisEndpoint endpoint, RevisionState revision, SeededJitter jitter, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(revision);

        _pool = new RedisConnectionPool(endpoint, OrderRevisions.ServiceName, size: 4, maxLifetime: TimeSpan.FromSeconds(5), TimeSpan.FromMilliseconds(500), time);
        _revision = revision;
        _jitter = jitter;
        _time = time;
        revision.OnChanged(_ => _pool.ResetAsync());
    }

    public int PoolConnections => _pool.OpenConnections;

    /// <summary>The lookup latency the serving revision produces right now, before jitter.</summary>
    public static TimeSpan QueryLatency(string revision, TimeSpan uptime)
    {
        if (revision != OrderRevisions.SlowQuery)
        {
            return BaseQueryLatency;
        }

        TimeSpan grown = BaseQueryLatency + SlowQueryGrowthPerSecond * uptime.TotalSeconds;
        return grown < MaxQueryLatency ? grown : MaxQueryLatency;
    }

    public async Task<TimeSpan> LookUpAsync(string cartId, CancellationToken cancellationToken)
    {
        using Activity? activity = OrderTelemetry.Activities.StartActivity("SELECT orders", ActivityKind.Client);
        activity?.SetTag("db.system.name", "postgresql");
        activity?.SetTag("db.query.summary", "SELECT orders by cart");
        activity?.SetTag("cart.id", cartId);

        TimeSpan latency = _jitter.Vary(QueryLatency(_revision.Current, _revision.Uptime), 0.1);
        await Task.Delay(latency, _time, cancellationToken);
        return latency;
    }

    public async Task RecordIdempotencyKeyAsync(string orderId, CancellationToken cancellationToken)
    {
        using Activity? activity = OrderTelemetry.Activities.StartActivity("redis SET", ActivityKind.Client);
        activity?.SetTag("db.system.name", "redis");
        await _pool.ExecuteAsync(cancellationToken, "SET", $"order:{orderId}", "placed", "EX", "300");
    }

    public ValueTask DisposeAsync() => _pool.DisposeAsync();
}

public sealed class OrderProcessor(OrderStore store, RevisionState revision, OrderTelemetry telemetry, TimeProvider time, ILogger<OrderProcessor> logger)
{
    public async Task<OrderResult?> PlaceAsync(string cartId, CancellationToken cancellationToken)
    {
        long started = time.GetTimestamp();
        string serving = revision.Current;
        var tags = new TagList { { "revision", serving } };
        string outcome = "success";
        OrderResult? result = null;

        try
        {
            TimeSpan query = await store.LookUpAsync(cartId, cancellationToken);
            telemetry.QueryDuration.Record(query.TotalSeconds, tags);
            if (query > TimeSpan.FromMilliseconds(500))
            {
                logger.LogWarning("Slow order lookup for cart {CartId}: {QueryMilliseconds} ms", cartId, (int)query.TotalMilliseconds);
            }

            string orderId = $"ord-{cartId}";
            await store.RecordIdempotencyKeyAsync(orderId, cancellationToken);
            result = new OrderResult(orderId, serving);
        }
        catch (RedisUnavailableException exception)
        {
            logger.LogError(exception, "Order for cart {CartId} failed on Redis: {Reason}", cartId, exception.InnerException?.Message ?? exception.Message);
            outcome = "redis_error";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The caller gave up, typically checkout's 600 ms attempt timeout.
            outcome = "cancelled";
        }

        tags.Add("outcome", outcome);
        telemetry.Requests.Add(1, tags);
        telemetry.Duration.Record(time.GetElapsedTime(started).TotalSeconds, tags);
        return result;
    }
}
