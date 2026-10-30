using Reference.QueueConsumer.Queue;
using Reference.QueueConsumer.Telemetry;

namespace Reference.QueueConsumer.Processing;

/// <summary>
/// Six workers receiving from the orders queue. Each books its order and confirms it with order-service. A message that
/// fails holds its worker for the visibility timeout, as a consumer that times out on it would, and then returns to the
/// queue to be received again.
/// </summary>
public sealed class QueueWorker(IOrderQueue queue, OrderServiceClient orders, QueueTelemetry telemetry, TimeProvider time, ILogger<QueueWorker> logger) : BackgroundService
{
    private const int Workers = 6;

    public static readonly TimeSpan VisibilityTimeout = TimeSpan.FromSeconds(2);

    private static readonly TimeSpan IdleDelay = TimeSpan.FromMilliseconds(100);

    // Settling a processed message is retried, briefly, before the worker abandons it to be reclaimed.
    private const int SettleAttempts = 5;
    private static readonly TimeSpan SettleRetryDelay = TimeSpan.FromMilliseconds(200);

    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        Task.WhenAll(Enumerable.Range(0, Workers).Select(_ => RunAsync(stoppingToken)));

    private async Task RunAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (await queue.ReceiveAsync(stoppingToken) is not { } received)
                {
                    await Task.Delay(IdleDelay, time, stoppingToken);
                    continue;
                }

                try
                {
                    await OrderHandler.HandleAsync(received.Message, stoppingToken);
                    if (await queue.BookAsync(received.Message, stoppingToken) > 1)
                    {
                        telemetry.Duplicate();
                        logger.LogWarning("Order {OrderId} was booked again from message {MessageId}; it had been booked already", received.Message.OrderId, received.Message.Id);
                    }

                    await orders.ConfirmAsync(received.Message, stoppingToken);
                    await SettleAsync(received, () => queue.CompleteAsync(received, stoppingToken), stoppingToken);
                    telemetry.Processed();
                }
                catch (FormatException exception)
                {
                    telemetry.Failed();
                    logger.LogWarning(exception, "Order message {MessageId} failed on receive {Receive}: {Error}", received.Message.Id, received.Receive, exception.Message);
                    await Task.Delay(VisibilityTimeout, time, stoppingToken);
                    await SettleAsync(received, () => queue.ReturnAsync(received, stoppingToken), stoppingToken);
                }
                catch (QueueUnavailableException)
                {
                    // Booking failed: nothing settled the message, so it is redelivered once it is found abandoned.
                    queue.Abandon(received);
                    throw;
                }
            }
            catch (QueueUnavailableException exception)
            {
                logger.LogWarning(exception, "The orders queue is unavailable; retrying");
                await Task.Delay(TimeSpan.FromSeconds(1), time, stoppingToken);
            }
        }
    }

    /// <summary>Completes or returns a message, retrying briefly; a worker that still cannot settle it abandons it.</summary>
    private async Task SettleAsync(ReceivedOrder received, Func<Task> settle, CancellationToken stoppingToken)
    {
        for (int attempt = 1; attempt <= SettleAttempts; attempt++)
        {
            try
            {
                await settle();
                return;
            }
            catch (QueueUnavailableException) when (attempt < SettleAttempts)
            {
                await Task.Delay(SettleRetryDelay, time, stoppingToken);
            }
            catch (QueueUnavailableException)
            {
                queue.Abandon(received);
                throw;
            }
        }
    }
}
