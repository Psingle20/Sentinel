using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using Reference.Common.Redis;
using Reference.Common.Revisions;
using Reference.Common.Telemetry;
using Reference.Checkout.Orders;
using Reference.Checkout.Redis;
using Reference.Checkout.Telemetry;

namespace Reference.Checkout.Processing;

/// <summary>
/// One checkout: write the cart to Redis, then place the order, retrying order-service up to
/// three times. The retries are what amplify an order-service slowdown in S1 and S3.
/// </summary>
public sealed class CheckoutProcessor(
    CheckoutRedis redis,
    OrderServiceClient orders,
    RevisionState revision,
    CheckoutTelemetry telemetry,
    TimeProvider time,
    ILogger<CheckoutProcessor> logger)
{
    public const int MaxOrderAttempts = 3;

    public static readonly TimeSpan SlowThreshold = TimeSpan.FromSeconds(1);

    public async Task<CheckoutResult> ProcessAsync(string cartId, CancellationToken cancellationToken)
    {
        long started = time.GetTimestamp();
        string serving = revision.Current;
        var tags = new TagList { { "revision", serving } };
        string outcome;

        try
        {
            await redis.TouchSessionAsync(cartId, serving, cancellationToken);
            outcome = await PlaceOrderWithRetriesAsync(cartId, tags, cancellationToken) ? "success" : "order_failed";
        }
        catch (RedisUnavailableException exception)
        {
            telemetry.RedisErrors.Add(1, tags);
            logger.LogError(exception, "Checkout {CartId} failed on Redis: {Reason}", cartId, exception.InnerException?.Message ?? exception.Message);
            outcome = "redis_error";
        }

        TimeSpan elapsed = time.GetElapsedTime(started);
        if (outcome == "success" && elapsed > SlowThreshold)
        {
            outcome = "slow";
        }

        tags.Add("outcome", outcome);
        telemetry.Requests.Add(1, tags);
        telemetry.Duration.Record(elapsed.TotalSeconds, tags);
        return new CheckoutResult(cartId, outcome, serving);
    }

    private async Task<bool> PlaceOrderWithRetriesAsync(string cartId, TagList tags, CancellationToken cancellationToken)
    {
        for (int attempt = 1; attempt <= MaxOrderAttempts; attempt++)
        {
            if (attempt > 1)
            {
                telemetry.OrderRetries.Add(1, tags);
            }

            long waitStarted = time.GetTimestamp();
            bool placed = await orders.PlaceOrderAsync(cartId, cancellationToken);
            telemetry.OrderWait.Record(time.GetElapsedTime(waitStarted).TotalSeconds, tags);
            if (placed)
            {
                return true;
            }
        }

        logger.LogWarning("Checkout {CartId} gave up on order-service after {Attempts} attempts", cartId, MaxOrderAttempts);
        return false;
    }
}
