using System.Globalization;
using Reference.Common;
using Reference.Common.Redis;
using Reference.QueueConsumer.Queue;

namespace Reference.QueueConsumer.Processing;

/// <summary>The upstream that keeps publishing orders, a few per second, so the consumer always has healthy work.</summary>
public sealed class OrderProducer(OrderQueue queue, SeededJitter jitter, TimeProvider time, ILogger<OrderProducer> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(250);

    private long _sequence;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                DateTimeOffset now = time.GetUtcNow();
                string amount = (50 + 40 * jitter.NextSigned()).ToString("0.00", CultureInfo.InvariantCulture);
                await queue.EnqueueAsync(new OrderMessage($"ord-{now.ToUnixTimeMilliseconds()}-{Interlocked.Increment(ref _sequence)}", amount, now), stoppingToken);
            }
            catch (RedisUnavailableException exception)
            {
                logger.LogWarning(exception, "Could not publish an order; retrying");
            }

            await Task.Delay(Interval, time, stoppingToken);
        }
    }
}
