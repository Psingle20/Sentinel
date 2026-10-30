using System.Globalization;
using Reference.Common;
using Reference.QueueConsumer.Faults;
using Reference.QueueConsumer.Queue;

namespace Reference.QueueConsumer.Processing;

/// <summary>The upstream that keeps publishing orders, a few per second, so the consumer always has healthy work.</summary>
public sealed class OrderProducer(IOrderQueue queue, DuplicatePublication duplicates, SeededJitter jitter, TimeProvider time, ILogger<OrderProducer> logger) : BackgroundService
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
                long sequence = Interlocked.Increment(ref _sequence);
                var order = new OrderMessage($"msg-{now.ToUnixTimeMilliseconds()}-{sequence}", $"ord-{now.ToUnixTimeMilliseconds()}-{sequence}", amount, now);
                await queue.EnqueueAsync(order, stoppingToken);
                if (duplicates.Active && sequence % 2 == 0)
                {
                    await queue.EnqueueAsync(order with { Id = $"{order.Id}-retry" }, stoppingToken);
                }
            }
            catch (QueueUnavailableException exception)
            {
                logger.LogWarning(exception, "Could not publish an order; retrying");
            }

            await Task.Delay(Interval, time, stoppingToken);
        }
    }
}
