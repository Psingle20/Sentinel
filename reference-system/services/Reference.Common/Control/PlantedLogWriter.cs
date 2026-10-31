using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Reference.Common.Control;

/// <summary>Writes the planted log line as a warning every few seconds while it lasts.</summary>
public sealed partial class PlantedLogWriter(PlantedLog planted, TimeProvider time, ILogger<PlantedLogWriter> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(3);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            if (planted.Current is { } message)
            {
                LogPlanted(message);
            }

            await Task.Delay(Interval, time, stoppingToken).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "{Message}")]
    private partial void LogPlanted(string message);
}
