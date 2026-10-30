using Reference.Common.Redis;
using Reference.QueueConsumer.Queue;

namespace Reference.QueueConsumer.Telemetry;

/// <summary>Reads the queue's status every two seconds for the gauges.</summary>
public sealed class QueueSampler(OrderQueue queue, QueueTelemetry telemetry, TimeProvider time, ILogger<QueueSampler> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(2);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                telemetry.Record(await queue.StatusAsync(stoppingToken));
            }
            catch (RedisUnavailableException exception)
            {
                logger.LogWarning(exception, "Could not sample the orders queue");
            }

            await Task.Delay(Interval, time, stoppingToken);
        }
    }
}
