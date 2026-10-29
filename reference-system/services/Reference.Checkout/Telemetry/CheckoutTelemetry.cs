using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using Reference.Common.Redis;
using Reference.Common.Revisions;
using Reference.Common.Telemetry;
using Reference.Checkout.Redis;

namespace Reference.Checkout.Telemetry;

/// <summary>Checkout's metrics and traces. Every measurement carries the serving revision.</summary>
public sealed class CheckoutTelemetry : IDisposable
{
    public const string SourceName = "Reference.Checkout";

    private readonly Meter _meter = new(SourceName);

    public CheckoutTelemetry(CheckoutRedis redis)
    {
        ArgumentNullException.ThrowIfNull(redis);

        Requests = _meter.CreateCounter<long>("reference.checkout.requests", "{request}", "Checkout requests by outcome.");
        Duration = _meter.CreateHistogram("reference.checkout.duration", "s", "End-to-end checkout duration.", tags: null, LatencyBuckets.Seconds);
        OrderWait = _meter.CreateHistogram("reference.checkout.order.wait", "s", "Time checkout spends waiting on order-service, per attempt.", tags: null, LatencyBuckets.Seconds);
        OrderRetries = _meter.CreateCounter<long>("reference.checkout.order.retries", "{retry}", "Retried order-service calls.");
        RedisErrors = _meter.CreateCounter<long>("reference.checkout.redis.errors", "{error}", "Failed Redis operations.");
        _meter.CreateObservableGauge("reference.checkout.redis.pool.connections", () => redis.PoolConnections, "{connection}", "Open pooled Redis connections.");
        _meter.CreateObservableGauge("reference.checkout.redis.leaked.connections", () => redis.LeakedConnections, "{connection}", "Redis connections opened and never returned.");
    }

    public static ActivitySource Activities { get; } = new(SourceName);

    public Counter<long> Requests { get; }

    public Histogram<double> Duration { get; }

    public Histogram<double> OrderWait { get; }

    public Counter<long> OrderRetries { get; }

    public Counter<long> RedisErrors { get; }

    public void Dispose() => _meter.Dispose();
}
