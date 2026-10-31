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

    /// <summary>v1.7.2's Redis pool: ten times the connections, kept open longer, so they hold steady instead of growing.</summary>
    public const int WidePoolSize = 40;

    private readonly RedisConnectionPool _pool;
    private readonly RedisConnectionPool _widePool;
    private readonly RevisionState _revision;
    private readonly SeededJitter _jitter;
    private readonly TimeProvider _time;

    public OrderStore(RedisEndpoint endpoint, RevisionState revision, SeededJitter jitter, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(revision);

        _pool = new RedisConnectionPool(endpoint, OrderRevisions.ServiceName, size: 4, maxLifetime: TimeSpan.FromSeconds(5), TimeSpan.FromMilliseconds(500), time);
        _widePool = new RedisConnectionPool(endpoint, OrderRevisions.ServiceName, size: WidePoolSize, maxLifetime: TimeSpan.FromSeconds(30), TimeSpan.FromMilliseconds(500), time);
        _revision = revision;
        _jitter = jitter;
        _time = time;
        revision.OnChanged(async _ =>
        {
            await _pool.ResetAsync();
            await _widePool.ResetAsync();
        });
    }

    public int PoolConnections => _pool.OpenConnections + _widePool.OpenConnections;

    /// <summary>The lookup latency the serving revision produces right now, before jitter.</summary>
    public static TimeSpan QueryLatency(string revision, TimeSpan uptime) => SlowOrderLookup.Latency(revision, uptime);

    public async Task<TimeSpan> LookUpAsync(string cartId, CancellationToken cancellationToken)
    {
        using Activity? activity = OrderTelemetry.Activities.StartActivity("SELECT orders", ActivityKind.Client);
        activity?.SetTag("db.system.name", "postgresql");
        activity?.SetTag("db.query.summary", "SELECT orders by cart");
        activity?.SetTag("cart.id", cartId);

        double draw = (_jitter.NextSigned() + 1) / 2;
        TimeSpan latency = _jitter.Vary(SlowOrderLookup.Latency(_revision.Current, _revision.Uptime, draw), 0.1);
        await Task.Delay(latency, _time, cancellationToken);
        return latency;
    }

    public async Task RecordIdempotencyKeyAsync(string orderId, CancellationToken cancellationToken)
    {
        using Activity? activity = OrderTelemetry.Activities.StartActivity("redis SET", ActivityKind.Client);
        activity?.SetTag("db.system.name", "redis");
        RedisConnectionPool pool = _revision.Current == OrderRevisions.WidePool ? _widePool : _pool;
        await pool.ExecuteAsync(cancellationToken, "SET", $"order:{orderId}", "placed", "EX", "300");
    }

    public async ValueTask DisposeAsync()
    {
        await _pool.DisposeAsync();
        await _widePool.DisposeAsync();
    }
}
