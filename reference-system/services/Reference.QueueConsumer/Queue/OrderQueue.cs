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
/// </summary>
public sealed class OrderQueue : IAsyncDisposable
{
    public const string Name = "orders";

    private const string Waiting = "queue:orders";
    private const string InFlight = "queue:orders:in-flight";
    private const string Quarantine = "queue:orders:quarantine";
    private const string QuarantinedIds = "queue:orders:quarantined-ids";
    private const string Receives = "queue:orders:receives";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly RedisConnectionPool _pool;
    private readonly TimeProvider _time;

    public OrderQueue(RedisEndpoint endpoint, TimeProvider time)
    {
        _time = time;
        _pool = new RedisConnectionPool(endpoint, QueueRevisions.ServiceName, size: 4, maxLifetime: TimeSpan.FromMinutes(5), TimeSpan.FromMilliseconds(500), time);
    }

    public async Task EnqueueAsync(OrderMessage message, CancellationToken cancellationToken) =>
        await _pool.ExecuteAsync(cancellationToken, "LPUSH", Waiting, JsonSerializer.Serialize(message, Json));

    /// <summary>Takes the oldest waiting message into flight and counts the receive, or returns null when none waits.</summary>
    public async Task<(OrderMessage Message, string Raw, int Receive)?> ReceiveAsync(CancellationToken cancellationToken)
    {
        string? raw = await _pool.ExecuteAsync(cancellationToken, "LMOVE", Waiting, InFlight, "RIGHT", "LEFT");
        if (raw is null)
        {
            return null;
        }

        OrderMessage message = JsonSerializer.Deserialize<OrderMessage>(raw, Json)!;
        if (await IsQuarantinedAsync(message.Id, cancellationToken))
        {
            await _pool.ExecuteAsync(cancellationToken, "LREM", InFlight, "1", raw);
            return null;
        }

        int receive = int.Parse(await _pool.ExecuteAsync(cancellationToken, "HINCRBY", Receives, message.Id, "1") ?? "1", CultureInfo.InvariantCulture);
        return (message, raw, receive);
    }

    /// <summary>The message was processed: it leaves the queue.</summary>
    public async Task CompleteAsync(OrderMessage message, string raw, CancellationToken cancellationToken)
    {
        await _pool.ExecuteAsync(cancellationToken, "LREM", InFlight, "1", raw);
        await _pool.ExecuteAsync(cancellationToken, "HDEL", Receives, message.Id);
    }

    /// <summary>Processing failed: the message waits to be received again, unless it was quarantined meanwhile.</summary>
    public async Task ReturnAsync(OrderMessage message, string raw, CancellationToken cancellationToken)
    {
        await _pool.ExecuteAsync(cancellationToken, "LREM", InFlight, "1", raw);
        if (!await IsQuarantinedAsync(message.Id, cancellationToken))
        {
            await _pool.ExecuteAsync(cancellationToken, "LPUSH", Waiting, raw);
        }
    }

    /// <summary>Moves the messages received at least <c>MinimumReceives</c> times to the quarantine, at most <c>MaxMessages</c>.</summary>
    public async Task<QuarantineResult> QuarantineAsync(QuarantineRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Dictionary<string, int> receives = await ReceivesAsync(cancellationToken);
        string[] candidates = [.. receives.Where(entry => entry.Value >= request.MinimumReceives).OrderByDescending(entry => entry.Value).Take(request.MaxMessages).Select(entry => entry.Key)];
        IReadOnlyList<string> waiting = await RangeAsync(Waiting, cancellationToken);
        IReadOnlyList<string> inFlight = await RangeAsync(InFlight, cancellationToken);
        foreach (string id in candidates)
        {
            await _pool.ExecuteAsync(cancellationToken, "SADD", QuarantinedIds, id);
            string? raw = waiting.Concat(inFlight).FirstOrDefault(entry => IdOf(entry) == id);
            if (raw is not null)
            {
                await _pool.ExecuteAsync(cancellationToken, "LREM", Waiting, "1", raw);
                await _pool.ExecuteAsync(cancellationToken, "RPUSH", Quarantine, raw);
            }

            await _pool.ExecuteAsync(cancellationToken, "HDEL", Receives, id);
        }

        return new QuarantineResult(candidates);
    }

    /// <summary>Returns every quarantined message to the queue with its receive count cleared: the quarantine's compensation.</summary>
    public async Task<QuarantineResult> ReleaseAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<string> quarantined = await RangeAsync(Quarantine, cancellationToken);
        foreach (string raw in quarantined)
        {
            await _pool.ExecuteAsync(cancellationToken, "LPUSH", Waiting, raw);
        }

        await _pool.ExecuteAsync(cancellationToken, "DEL", Quarantine, QuarantinedIds);
        return new QuarantineResult([.. quarantined.Select(IdOf)]);
    }

    /// <summary>Empties the queue entirely, for a scenario reset.</summary>
    public async Task ResetAsync(CancellationToken cancellationToken) =>
        await _pool.ExecuteAsync(cancellationToken, "DEL", Waiting, InFlight, Quarantine, QuarantinedIds, Receives);

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

        return new QueueStatus(Name, waiting.Count, inFlight.Count, quarantined.Count, Math.Max(0, oldest), receives.Values.DefaultIfEmpty(0).Max(), redelivered);
    }

    /// <summary>One message's state: waiting, in flight, quarantined, or unknown once it was processed or never existed.</summary>
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

    private async Task<bool> IsQuarantinedAsync(string id, CancellationToken cancellationToken) =>
        await _pool.ExecuteAsync(cancellationToken, "SISMEMBER", QuarantinedIds, id) == "1";

    // Messages are single-line JSON, so the flattened reply of a list splits on line breaks.
    private async Task<IReadOnlyList<string>> RangeAsync(string key, CancellationToken cancellationToken) =>
        (await _pool.ExecuteAsync(cancellationToken, "LRANGE", key, "0", "999"))?.Split('\n', StringSplitOptions.RemoveEmptyEntries) ?? [];

    private async Task<Dictionary<string, int>> ReceivesAsync(CancellationToken cancellationToken)
    {
        string[] flat = (await _pool.ExecuteAsync(cancellationToken, "HGETALL", Receives))?.Split('\n') ?? [];
        var receives = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int index = 0; index + 1 < flat.Length; index += 2)
        {
            receives[flat[index]] = int.Parse(flat[index + 1], CultureInfo.InvariantCulture);
        }

        return receives;
    }

    private static string IdOf(string raw) => JsonSerializer.Deserialize<OrderMessage>(raw, Json)!.Id;
}
