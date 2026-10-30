using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using Reference.Common.Redis;
using Reference.QueueConsumer.Revisions;

namespace Reference.QueueConsumer.Queue;

/// <summary>
/// The orders queue in the reference Redis, kept as a reliable queue: a worker moves a message from the waiting list to
/// the in-flight list, and either removes it once processed or, when processing failed, returns it to the waiting list,
/// as a visibility timeout returns an SQS message. Every receive is counted, so a message that keeps failing shows a
/// rising receive count while the others pass through. Quarantined messages leave the queue for a list of their own.
/// The consumer is the queue's only reader, so it knows which in-flight messages its workers hold; one found in flight
/// and unheld at two samples in a row was abandoned, by a lost reply or a worker that could not settle it, and returns
/// to the front of the queue, as its visibility timeout would return an SQS message.
/// </summary>
public sealed class RedisOrderQueue : IOrderQueue, IAsyncDisposable
{
    private const string Waiting = "queue:orders";
    private const string InFlight = "queue:orders:in-flight";
    private const string Quarantine = "queue:orders:quarantine";
    private const string QuarantinedIds = "queue:orders:quarantined-ids";
    private const string Receives = "queue:orders:receives";
    private const string Bookings = "queue:orders:bookings";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly RedisConnectionPool _pool;
    private readonly TimeProvider _time;
    private readonly ConcurrentDictionary<string, byte> _held = new(StringComparer.Ordinal);
    private HashSet<string> _unheld = new(StringComparer.Ordinal);

    public RedisOrderQueue(RedisEndpoint endpoint, TimeProvider time)
    {
        _time = time;
        _pool = new RedisConnectionPool(endpoint, QueueRevisions.ServiceName, size: 8, maxLifetime: TimeSpan.FromMinutes(5), TimeSpan.FromMilliseconds(500), time);
    }

    public async Task EnqueueAsync(OrderMessage message, CancellationToken cancellationToken) =>
        await ExecuteAsync(cancellationToken, "LPUSH", Waiting, JsonSerializer.Serialize(message, Json));

    public async Task<ReceivedOrder?> ReceiveAsync(CancellationToken cancellationToken)
    {
        string? raw = await ExecuteAsync(cancellationToken, "LMOVE", Waiting, InFlight, "RIGHT", "LEFT");
        if (raw is null)
        {
            return null;
        }

        // Held from here until a worker settles it. If the receive fails half-way, no worker has the message, so the hold
        // is released and the message is reclaimed like any other abandoned one.
        _held[raw] = 0;
        try
        {
            OrderMessage message = JsonSerializer.Deserialize<OrderMessage>(raw, Json)!;
            if (await IsQuarantinedAsync(message.Id, cancellationToken))
            {
                await ExecuteAsync(cancellationToken, "LREM", InFlight, "1", raw);
                _held.TryRemove(raw, out _);
                return null;
            }

            int receive = int.Parse(await ExecuteAsync(cancellationToken, "HINCRBY", Receives, message.Id, "1") ?? "1", CultureInfo.InvariantCulture);
            return new ReceivedOrder(message, raw, receive);
        }
        catch
        {
            _held.TryRemove(raw, out _);
            throw;
        }
    }

    public async Task CompleteAsync(ReceivedOrder received, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(received);

        await ExecuteAsync(cancellationToken, "LREM", InFlight, "1", received.Handle);
        await ExecuteAsync(cancellationToken, "HDEL", Receives, received.Message.Id);
        _held.TryRemove(received.Handle, out _);
    }

    public async Task<int> BookAsync(OrderMessage message, CancellationToken cancellationToken) =>
        int.Parse(await ExecuteAsync(cancellationToken, "HINCRBY", Bookings, message.OrderId, "1") ?? "1", CultureInfo.InvariantCulture);

    public async Task ReturnAsync(ReceivedOrder received, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(received);

        await ExecuteAsync(cancellationToken, "LREM", InFlight, "1", received.Handle);
        _held.TryRemove(received.Handle, out _);
        if (!await IsQuarantinedAsync(received.Message.Id, cancellationToken))
        {
            await ExecuteAsync(cancellationToken, "LPUSH", Waiting, received.Handle);
        }
    }

    public void Abandon(ReceivedOrder received)
    {
        ArgumentNullException.ThrowIfNull(received);

        _held.TryRemove(received.Handle, out _);
    }

    public async Task ReclaimAsync(CancellationToken cancellationToken)
    {
        HashSet<string> unheld = [.. (await RangeAsync(InFlight, cancellationToken)).Where(raw => !_held.ContainsKey(raw))];
        foreach (string raw in unheld.Where(_unheld.Contains))
        {
            if (await ExecuteAsync(cancellationToken, "LREM", InFlight, "1", raw) == "1")
            {
                await ExecuteAsync(cancellationToken, "RPUSH", Waiting, raw);
            }

            unheld.Remove(raw);
        }

        _unheld = unheld;
    }

    public async Task<QuarantineResult> QuarantineAsync(QuarantineRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Dictionary<string, int> receives = await ReceivesAsync(cancellationToken);
        string[] candidates = [.. receives.Where(entry => entry.Value >= request.MinimumReceives).OrderByDescending(entry => entry.Value).Take(request.MaxMessages).Select(entry => entry.Key)];
        IReadOnlyList<string> waiting = await RangeAsync(Waiting, cancellationToken);
        IReadOnlyList<string> inFlight = await RangeAsync(InFlight, cancellationToken);
        foreach (string id in candidates)
        {
            await ExecuteAsync(cancellationToken, "SADD", QuarantinedIds, id);
            string? raw = waiting.Concat(inFlight).FirstOrDefault(entry => IdOf(entry) == id);
            if (raw is not null)
            {
                await ExecuteAsync(cancellationToken, "LREM", Waiting, "1", raw);
                await ExecuteAsync(cancellationToken, "RPUSH", Quarantine, raw);
            }

            await ExecuteAsync(cancellationToken, "HDEL", Receives, id);
        }

        return new QuarantineResult(candidates);
    }

    /// <summary>Returns every quarantined message to the queue with its receive count cleared.</summary>
    public async Task<QuarantineResult> ReleaseAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<string> quarantined = await RangeAsync(Quarantine, cancellationToken);
        foreach (string raw in quarantined)
        {
            await ExecuteAsync(cancellationToken, "LPUSH", Waiting, raw);
        }

        await ExecuteAsync(cancellationToken, "DEL", Quarantine, QuarantinedIds);
        return new QuarantineResult([.. quarantined.Select(IdOf)]);
    }

    public async Task ResetAsync(CancellationToken cancellationToken) =>
        await ExecuteAsync(cancellationToken, "DEL", Waiting, InFlight, Quarantine, QuarantinedIds, Receives, Bookings);

    public async Task<QueueStatus> StatusAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<string> waiting = await RangeAsync(Waiting, cancellationToken);
        IReadOnlyList<string> inFlight = await RangeAsync(InFlight, cancellationToken);
        IReadOnlyList<string> quarantined = await RangeAsync(Quarantine, cancellationToken);
        Dictionary<string, int> receives = await ReceivesAsync(cancellationToken);

        DateTimeOffset now = _time.GetUtcNow();
        double oldest = waiting.Concat(inFlight)
            .Select(raw => (now - JsonSerializer.Deserialize<OrderMessage>(raw, Json)!.EnqueuedAt).TotalSeconds)
            .DefaultIfEmpty(0)
            .Max();
        HashSet<string> flying = [.. inFlight.Select(IdOf)];
        MessageView[] redelivered = [.. receives
            .Where(entry => entry.Value > 1)
            .OrderByDescending(entry => entry.Value)
            .Select(entry => new MessageView(entry.Key, entry.Value, flying.Contains(entry.Key) ? "in-flight" : "waiting"))];

        return new QueueStatus(IOrderQueue.Name, waiting.Count, inFlight.Count, quarantined.Count, Math.Max(0, oldest), receives.Values.DefaultIfEmpty(0).Max(), redelivered);
    }

    public async Task<MessageView> MessageAsync(string id, CancellationToken cancellationToken)
    {
        if (await IsQuarantinedAsync(id, cancellationToken))
        {
            return new MessageView(id, 0, "quarantined");
        }

        Dictionary<string, int> receives = await ReceivesAsync(cancellationToken);
        int count = receives.GetValueOrDefault(id);
        if ((await RangeAsync(InFlight, cancellationToken)).Any(raw => IdOf(raw) == id))
        {
            return new MessageView(id, count, "in-flight");
        }

        return (await RangeAsync(Waiting, cancellationToken)).Any(raw => IdOf(raw) == id)
            ? new MessageView(id, count, "waiting")
            : new MessageView(id, count, "unknown");
    }

    public async ValueTask DisposeAsync() => await _pool.DisposeAsync();

    private async Task<string?> ExecuteAsync(CancellationToken cancellationToken, params string[] command)
    {
        try
        {
            return await _pool.ExecuteAsync(cancellationToken, command);
        }
        catch (RedisUnavailableException exception)
        {
            throw new QueueUnavailableException("The orders queue in Redis is unavailable.", exception);
        }
    }

    private async Task<bool> IsQuarantinedAsync(string id, CancellationToken cancellationToken) =>
        await ExecuteAsync(cancellationToken, "SISMEMBER", QuarantinedIds, id) == "1";

    // Messages are single-line JSON, so the flattened reply of a list splits on line breaks.
    private async Task<IReadOnlyList<string>> RangeAsync(string key, CancellationToken cancellationToken) =>
        (await ExecuteAsync(cancellationToken, "LRANGE", key, "0", "999"))?.Split('\n', StringSplitOptions.RemoveEmptyEntries) ?? [];

    private async Task<Dictionary<string, int>> ReceivesAsync(CancellationToken cancellationToken)
    {
        string[] flat = (await ExecuteAsync(cancellationToken, "HGETALL", Receives))?.Split('\n') ?? [];
        var receives = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int index = 0; index + 1 < flat.Length; index += 2)
        {
            receives[flat[index]] = int.Parse(flat[index + 1], CultureInfo.InvariantCulture);
        }

        return receives;
    }

    private static string IdOf(string raw) => JsonSerializer.Deserialize<OrderMessage>(raw, Json)!.Id;
}
