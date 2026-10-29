using System.Diagnostics;
using System.Diagnostics.Metrics;
using Reference.Common;
using Reference.Common.Redis;
using Reference.Common.Revisions;
using Reference.Common.Telemetry;
using Reference.Orders.Revisions;
using Reference.Orders.Telemetry;

namespace Reference.Orders.Storage;

/// <summary>Order persistence: a simulated database lookup plus an idempotency key in Redis.</summary>
public sealed class OrderStore : IAsyncDisposable
{
    public static readonly TimeSpan BaseQueryLatency = SlowOrderLookup.BaseQueryLatency;

    /// <summary>Added per second that v1.8.0 has been serving (<see cref="SlowOrderLookup"/>).</summary>
    public static readonly TimeSpan SlowQueryGrowthPerSecond = SlowOrderLookup.GrowthPerSecond;

    public static readonly TimeSpan MaxQueryLatency = SlowOrderLookup.MaxQueryLatency;

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
    public static TimeSpan QueryLatency(string revision, TimeSpan uptime) => SlowOrderLookup.Latency(revision, uptime);

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
