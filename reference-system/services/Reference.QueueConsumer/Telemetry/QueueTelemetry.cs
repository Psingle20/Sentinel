using System.Diagnostics.Metrics;
using Reference.QueueConsumer.Queue;

namespace Reference.QueueConsumer.Telemetry;

/// <summary>
/// The consumer's signals: messages processed and failed, and the queue's depth, oldest-message age and highest receive
/// count, the same signals SQS reports. The gauges read the last sample, so collecting metrics never waits on Redis.
/// </summary>
public sealed class QueueTelemetry : IDisposable
{
    public const string SourceName = "Reference.QueueConsumer";

    private readonly Meter _meter = new(SourceName);
    private readonly Counter<long> _messages;
    private readonly Counter<long> _duplicates;
    private readonly Counter<long> _retries;
    private readonly Counter<long> _published;
    private QueueStatus _sample = new(IOrderQueue.Name, 0, 0, 0, 0, 0, []);

    public QueueTelemetry()
    {
        _messages = _meter.CreateCounter<long>("reference.queue.messages", "{message}", "Order messages received, by outcome.");
        _duplicates = _meter.CreateCounter<long>("reference.queue.duplicates", "{booking}", "Orders booked again although they were booked already.");
        _retries = _meter.CreateCounter<long>("reference.queue.retries", "{attempt}", "Order confirmations retried after an attempt gave up.");
        _published = _meter.CreateCounter<long>("reference.orders.published", "{order}", "Orders the producer published, by the route it published them on.");
        _meter.CreateObservableGauge("reference.queue.depth", () => _sample.Waiting + _sample.InFlight, "{message}", "Messages waiting or in flight.");
        _meter.CreateObservableGauge("reference.queue.oldest_message_age", () => _sample.OldestMessageAgeSeconds, "s", "Age of the oldest message still in the queue.");
        _meter.CreateObservableGauge("reference.queue.max_receive_count", () => _sample.MaxReceiveCount, "{receive}", "The highest receive count of a message still in the queue.");
        _meter.CreateObservableGauge("reference.queue.quarantined", () => _sample.Quarantined, "{message}", "Messages moved to quarantine.");
    }

    public QueueStatus Sample => Volatile.Read(ref _sample);

    public void Processed() => _messages.Add(1, new KeyValuePair<string, object?>("outcome", "processed"));

    public void Failed() => _messages.Add(1, new KeyValuePair<string, object?>("outcome", "failed"));

    public void Duplicate() => _duplicates.Add(1);

    public void Retried() => _retries.Add(1);

    public void Published(string route) => _published.Add(1, new KeyValuePair<string, object?>("route", route));

    public void Record(QueueStatus status) => Volatile.Write(ref _sample, status);

    public void Dispose() => _meter.Dispose();
}
