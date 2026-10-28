using System.Globalization;
using StackExchange.Redis;

namespace CleanArchitecture.BuildingBlocks.Infrastructure.Redis;

/// <summary>One dead-lettered entry: the message and why its consumer gave up on it.</summary>
public sealed record DeadLetter(
    string EntryId,
    string Stream,
    Guid? MessageId,
    string? MessageType,
    string Reason,
    string ConsumerGroup,
    string SourceEntryId,
    DateTimeOffset? FailedAt);

/// <summary>
/// Operator controls for <c>&lt;stream&gt;.dlq</c>: list what failed, and replay an entry after the
/// cause is fixed. A replayed message goes back to its source stream with its original message ID,
/// so consumers that already handled it treat it as a duplicate and only the failed consumer runs it.
/// </summary>
public sealed class RedisDeadLetters(IConnectionMultiplexer redis)
{
    private const string FieldPrefix = "dlq.";

    public async Task<IReadOnlyList<DeadLetter>> ListAsync(string stream, int count)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stream);

        StreamEntry[] entries = await redis.GetDatabase().StreamRangeAsync(stream + RedisStreamsConsumer.DeadLetterSuffix, count: count, messageOrder: Order.Descending);
        return [.. entries.Select(entry => ToDeadLetter(stream, entry))];
    }

    /// <summary>Moves a dead-lettered entry back to its stream. Returns false when the entry does not exist.</summary>
    public async Task<bool> ReplayAsync(string stream, string entryId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stream);
        ArgumentException.ThrowIfNullOrWhiteSpace(entryId);

        IDatabase database = redis.GetDatabase();
        string deadLetters = stream + RedisStreamsConsumer.DeadLetterSuffix;
        StreamEntry[] found = await database.StreamRangeAsync(deadLetters, entryId, entryId, count: 1);
        if (found.Length == 0)
        {
            return false;
        }

        NameValueEntry[] original = [.. found[0].Values.Where(field => !field.Name.ToString().StartsWith(FieldPrefix, StringComparison.Ordinal))];
        await database.StreamAddAsync(stream, original);
        await database.StreamDeleteAsync(deadLetters, [entryId]);
        return true;
    }

    private static DeadLetter ToDeadLetter(string stream, StreamEntry entry)
    {
        Dictionary<string, string> fields = entry.Values.ToDictionary(field => field.Name.ToString(), field => field.Value.ToString(), StringComparer.Ordinal);
        return new DeadLetter(
            entry.Id.ToString(),
            stream,
            Guid.TryParse(fields.GetValueOrDefault(RedisEnvelopeCodec.MessageIdField), out Guid messageId) ? messageId : null,
            fields.GetValueOrDefault(RedisEnvelopeCodec.MessageTypeField),
            fields.GetValueOrDefault("dlq.reason") ?? "unknown",
            fields.GetValueOrDefault("dlq.consumer-group") ?? "unknown",
            fields.GetValueOrDefault("dlq.source-entry") ?? string.Empty,
            DateTimeOffset.TryParse(fields.GetValueOrDefault("dlq.failed-at"), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTimeOffset failedAt) ? failedAt : null);
    }
}
