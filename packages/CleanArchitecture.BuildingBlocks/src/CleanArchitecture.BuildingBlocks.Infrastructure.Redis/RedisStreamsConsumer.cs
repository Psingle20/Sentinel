using System.Globalization;
using CleanArchitecture.BuildingBlocks.Infrastructure.Messaging;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace CleanArchitecture.BuildingBlocks.Infrastructure.Redis;

/// <summary>
/// Reads every stream the service consumes through a consumer group named after the service.
/// Entries are acknowledged only after their handling commits; entries left pending by a
/// crashed consumer are reclaimed, and entries that keep failing move to <c>&lt;stream&gt;.dlq</c>.
/// A group the broker lost, for example by restarting without its data, is created again.
/// </summary>
internal sealed class RedisStreamsConsumer(
    IConnectionMultiplexer redis,
    IInboundMessageProcessor processor,
    MessageTypeRegistry registry,
    MessagingStartupGate gate,
    IOptions<MessagingOptions> messagingOptions,
    IOptions<RedisStreamsOptions> options,
    TimeProvider timeProvider,
    ILogger<RedisStreamsConsumer> logger) : BackgroundService
{
    internal const string DeadLetterSuffix = ".dlq";

    private readonly RedisStreamsOptions _options = options.Value;
    private readonly string _group = messagingOptions.Value.ServiceName;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        string[] streams = [.. registry.Consumers.Select(consumer => consumer.Message.Stream).Distinct(StringComparer.Ordinal)];
        if (streams.Length == 0)
        {
            return;
        }

        await gate.WaitAsync(stoppingToken);
        IDatabase database = redis.GetDatabase();
        foreach (string stream in streams)
        {
            await EnsureGroupAsync(database, stream);
        }

        var lost = new HashSet<string>(StringComparer.Ordinal);
        while (!stoppingToken.IsCancellationRequested)
        {
            int handled = 0;
            foreach (string stream in streams)
            {
                try
                {
                    // Created again from the start of the stream, so what the stream still holds is delivered again and
                    // the inbox recognises what was already handled.
                    if (lost.Remove(stream))
                    {
                        await EnsureGroupAsync(database, stream);
                    }

                    handled += await PollAsync(database, stream, stoppingToken);
                }
                catch (RedisServerException exception) when (exception.Message.StartsWith("NOGROUP", StringComparison.Ordinal))
                {
                    logger.LogWarning(exception, "Consumer group {Group} on stream {Stream} is gone; it is created again", _group, stream);
                    lost.Add(stream);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    logger.LogError(exception, "Reading stream {Stream} failed; retrying after the poll interval", stream);
                }
            }

            if (handled == 0)
            {
                await Task.Delay(_options.PollInterval, timeProvider, stoppingToken);
            }
        }
    }

    internal async Task<int> PollAsync(IDatabase database, string stream, CancellationToken cancellationToken)
    {
        // Entries another consumer took but never acknowledged, for example because it crashed.
        StreamAutoClaimResult reclaimed = await database.StreamAutoClaimAsync(
            stream,
            _group,
            _options.ConsumerName,
            (long)_options.ClaimIdleTime.TotalMilliseconds,
            "0-0",
            _options.BatchSize);

        StreamEntry[] fresh = await database.StreamReadGroupAsync(stream, _group, _options.ConsumerName, ">", _options.BatchSize);

        StreamEntry[] entries = [.. reclaimed.ClaimedEntries.Where(entry => !entry.IsNull), .. fresh];
        if (_options.MaxConcurrency == 1)
        {
            foreach (StreamEntry entry in entries)
            {
                await HandleAsync(database, stream, entry, cancellationToken);
            }
        }
        else
        {
            await Parallel.ForEachAsync(
                entries,
                new ParallelOptions { MaxDegreeOfParallelism = _options.MaxConcurrency, CancellationToken = cancellationToken },
                async (entry, token) => await HandleAsync(database, stream, entry, token));
        }

        return entries.Length;
    }

    private async Task HandleAsync(IDatabase database, string stream, StreamEntry entry, CancellationToken cancellationToken)
    {
        MessageEnvelope envelope;
        try
        {
            envelope = RedisEnvelopeCodec.Decode(entry);
        }
        catch (FormatException exception)
        {
            await DeadLetterAsync(database, stream, entry, $"Malformed entry: {exception.Message}");
            return;
        }

        ProcessingResult result = await processor.ProcessAsync(envelope, cancellationToken);
        switch (result.Outcome)
        {
            case ProcessingOutcome.Processed:
            case ProcessingOutcome.Duplicate:
                await database.StreamAcknowledgeAsync(stream, _group, entry.Id);
                break;

            case ProcessingOutcome.Rejected:
                await DeadLetterAsync(database, stream, entry, result.Reason ?? "Rejected");
                break;

            case ProcessingOutcome.Failed:
                long deliveries = await DeliveryCountAsync(database, stream, entry.Id);
                if (deliveries >= _options.MaxDeliveries)
                {
                    await DeadLetterAsync(database, stream, entry, $"Failed after {deliveries} deliveries: {result.Reason}");
                }

                // Otherwise the entry stays pending and is reclaimed after ClaimIdleTime.
                break;

            default:
                throw new InvalidOperationException($"Unknown processing outcome {result.Outcome}.");
        }
    }

    private async Task<long> DeliveryCountAsync(IDatabase database, string stream, RedisValue entryId)
    {
        StreamPendingMessageInfo[] pending = await database.StreamPendingMessagesAsync(stream, _group, 1, RedisValue.Null, entryId, entryId);
        return pending.Length == 0 ? 1 : pending[0].DeliveryCount;
    }

    // Written before the acknowledgement: a crash in between duplicates the dead letter rather than losing it.
    private async Task DeadLetterAsync(IDatabase database, string stream, StreamEntry entry, string reason)
    {
        NameValueEntry[] fields =
        [
            .. entry.Values,
            new NameValueEntry("dlq.reason", reason),
            new NameValueEntry("dlq.source-entry", entry.Id),
            new NameValueEntry("dlq.consumer-group", _group),
            new NameValueEntry("dlq.failed-at", timeProvider.GetUtcNow().ToString("O", CultureInfo.InvariantCulture))
        ];

        await database.StreamAddAsync(stream + DeadLetterSuffix, fields);
        await database.StreamAcknowledgeAsync(stream, _group, entry.Id);
        logger.LogWarning("Dead-lettered entry {EntryId} from {Stream}: {Reason}", entry.Id, stream, reason);
    }

    private async Task EnsureGroupAsync(IDatabase database, string stream)
    {
        try
        {
            // Position 0 delivers entries published before the group first existed.
            await database.StreamCreateConsumerGroupAsync(stream, _group, "0", createStream: true);
        }
        catch (RedisServerException exception) when (exception.Message.StartsWith("BUSYGROUP", StringComparison.Ordinal))
        {
            // The group already exists.
        }
    }
}
