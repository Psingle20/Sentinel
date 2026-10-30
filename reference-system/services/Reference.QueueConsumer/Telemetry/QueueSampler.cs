using Reference.QueueConsumer.Queue;

namespace Reference.QueueConsumer.Telemetry;

/// <summary>Reads the queue's status every two seconds for the gauges, and returns the messages its workers abandoned.</summary>
public sealed class QueueSampler(IOrderQueue queue, QueueTelemetry telemetry, TimeProvider time, ILogger<QueueSampler> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(2);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                telemetry.Record(await queue.StatusAsync(stoppingToken));
                await queue.ReclaimAsync(stoppingToken);
            }
            catch (QueueUnavailableException exception)
            {
                logger.LogWarning(exception, "Could not sample the orders queue");
            }

            await Task.Delay(Interval, time, stoppingToken);
        }
    }
}
