using System.Diagnostics;
using System.Diagnostics.Metrics;
using Reference.Common;
using Reference.Common.Redis;
using Reference.Common.Revisions;
using Reference.Common.Telemetry;
using Reference.Orders.Storage;

namespace Reference.Orders.Telemetry;

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
        Confirmations = _meter.CreateCounter<long>("reference.orders.confirmations", "{confirmation}", "Order confirmations for the queue-consumer.");
        ConfirmationDuration = _meter.CreateHistogram("reference.orders.confirmation.duration", "s", "Order confirmation duration.", tags: null, LatencyBuckets.Seconds);
        _meter.CreateObservableGauge("reference.orders.redis.pool.connections", () => store.PoolConnections, "{connection}", "Open pooled Redis connections.");
    }

    public static ActivitySource Activities { get; } = new(SourceName);

    public Counter<long> Requests { get; }

    public Histogram<double> Duration { get; }

    public Histogram<double> QueryDuration { get; }

    public Counter<long> Confirmations { get; }

    public Histogram<double> ConfirmationDuration { get; }

    public void Dispose() => _meter.Dispose();
}
