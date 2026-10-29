using System.Diagnostics;
using System.Diagnostics.Metrics;
using Reference.Common;
using Reference.Common.Redis;
using Reference.Common.Revisions;
using Reference.Common.Telemetry;
using Reference.Orders.Storage;
using Reference.Orders.Telemetry;

namespace Reference.Orders.Processing;

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
