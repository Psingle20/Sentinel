using Reference.Common.Redis;
using Reference.QueueConsumer.Queue;
using Reference.QueueConsumer.Telemetry;

namespace Reference.QueueConsumer.Processing;

/// <summary>
/// Two workers receiving from the orders queue. A message that fails holds its worker for the visibility timeout, as a
/// consumer that times out on it would, and then returns to the queue to be received again.
/// </summary>
public sealed class QueueWorker(OrderQueue queue, QueueTelemetry telemetry, TimeProvider time, ILogger<QueueWorker> logger) : BackgroundService
{
    private const int Workers = 2;

    public static readonly TimeSpan VisibilityTimeout = TimeSpan.FromSeconds(2);

    private static readonly TimeSpan IdleDelay = TimeSpan.FromMilliseconds(100);

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
                    await queue.CompleteAsync(received.Message, received.Raw, stoppingToken);
                    telemetry.Processed();
                }
                catch (FormatException exception)
                {
                    telemetry.Failed();
                    logger.LogWarning(exception, "Order message {MessageId} failed on receive {Receive}: {Error}", received.Message.Id, received.Receive, exception.Message);
                    await Task.Delay(VisibilityTimeout, time, stoppingToken);
                    await queue.ReturnAsync(received.Message, received.Raw, stoppingToken);
                }
            }
            catch (RedisUnavailableException exception)
            {
                logger.LogWarning(exception, "The orders queue is unavailable; retrying");
                await Task.Delay(TimeSpan.FromSeconds(1), time, stoppingToken);
            }
        }
    }
}
