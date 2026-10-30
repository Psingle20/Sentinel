using System.Diagnostics;
using Reference.Common.Revisions;
using Reference.Orders.Faults;
using Reference.Orders.Telemetry;

namespace Reference.Orders.Processing;

/// <summary>
/// Confirms a booked order for the queue-consumer. The work does not watch the request's cancellation, so a confirmation
/// whose caller gave up still holds order-service for as long as it takes.
/// </summary>
public sealed class OrderConfirmer(SlowConfirmations slow, RevisionState revision, OrderTelemetry telemetry, TimeProvider time, ILogger<OrderConfirmer> logger)
{
    public async Task ConfirmAsync(string orderId)
    {
        long started = time.GetTimestamp();
        TimeSpan latency = slow.Latency();
        await Task.Delay(latency, time);
        if (latency > TimeSpan.FromMilliseconds(500))
        {
            logger.LogWarning("Slow order confirmation for {OrderId}: {ConfirmationMilliseconds} ms", orderId, (int)latency.TotalMilliseconds);
        }

        var tags = new TagList { { "revision", revision.Current }, { "outcome", "success" } };
        telemetry.Confirmations.Add(1, tags);
        telemetry.ConfirmationDuration.Record(time.GetElapsedTime(started).TotalSeconds, tags);
    }
}
