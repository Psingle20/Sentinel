using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CleanArchitecture.BuildingBlocks.Infrastructure.Messaging;

/// <summary>
/// Publishes committed outbox records. Transport failures are transient, so a record is retried
/// with capped exponential backoff and never dropped; a growing backlog shows in the outbox
/// health check instead.
/// </summary>
internal sealed class OutboxDispatcher(
    IOutboxStore store,
    IMessagePublisher publisher,
    MessagingStartupGate gate,
    IOptions<OutboxOptions> options,
    TimeProvider timeProvider,
    ILogger<OutboxDispatcher> logger) : BackgroundService
{
    private readonly OutboxOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await gate.WaitAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            int dispatched = 0;
            try
            {
                dispatched = await DispatchBatchAsync(stoppingToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogError(exception, "Outbox dispatch failed; retrying after the poll interval");
            }

            // A full batch suggests more work is waiting, so continue without delay.
            if (dispatched < _options.BatchSize)
            {
                await Task.Delay(_options.PollInterval, timeProvider, stoppingToken);
            }
        }
    }

    internal async Task<int> DispatchBatchAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<OutboxRecord> batch = await store.ClaimDueAsync(_options.BatchSize, _options.ClaimLease, cancellationToken);

        foreach (OutboxRecord record in batch)
        {
            try
            {
                await publisher.PublishAsync(record.Stream, record.Envelope, cancellationToken);
                await store.MarkPublishedAsync(record.Envelope.MessageId, cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                TimeSpan delay = RetryDelay(record.Attempts + 1, _options);
                await store.MarkFailedAsync(record.Envelope.MessageId, delay, exception.Message, cancellationToken);
                logger.LogWarning(
                    exception,
                    "Publishing {MessageType} {MessageId} failed on attempt {Attempt}; retrying in {Delay}",
                    record.Envelope.MessageType,
                    record.Envelope.MessageId,
                    record.Attempts + 1,
                    delay);
            }
        }

        return batch.Count;
    }

    internal static TimeSpan RetryDelay(int attempt, OutboxOptions options)
    {
        double factor = Math.Pow(2, Math.Min(attempt - 1, 30));
        double milliseconds = Math.Min(options.InitialRetryDelay.TotalMilliseconds * factor, options.MaxRetryDelay.TotalMilliseconds);
        return TimeSpan.FromMilliseconds(milliseconds);
    }
}
