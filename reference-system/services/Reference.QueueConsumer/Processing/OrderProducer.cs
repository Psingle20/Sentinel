using System.Globalization;
using Reference.Common;
using Reference.QueueConsumer.Faults;
using Reference.QueueConsumer.Queue;
using Reference.QueueConsumer.Telemetry;

namespace Reference.QueueConsumer.Processing;

/// <summary>
/// The upstream that keeps publishing orders, a few per second, so the consumer always has healthy work. It counts what it
/// published, so an order that never reaches the consumer shows as published but never processed.
/// </summary>
public sealed class OrderProducer(IOrderPublisher publisher, QueueTelemetry telemetry, DuplicatePublication duplicates, SeededJitter jitter, TimeProvider time, ILogger<OrderProducer> logger) : BackgroundService
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
                await publisher.PublishAsync(order, stoppingToken);
                telemetry.Published(publisher.Route);
                if (duplicates.Active && sequence % 2 == 0)
                {
                    await publisher.PublishAsync(order with { Id = $"{order.Id}-retry" }, stoppingToken);
                    telemetry.Published(publisher.Route);
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
