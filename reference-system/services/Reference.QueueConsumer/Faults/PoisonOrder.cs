using Reference.QueueConsumer.Queue;

namespace Reference.QueueConsumer.Faults;

/// <summary>
/// The injected fault of S6: one order whose amount is not a number. No revision of the consumer can process it, so it
/// fails on every receive and keeps returning to the queue, while the orders around it are processed as usual.
/// </summary>
public static class PoisonOrder
{
    public const string Amount = "NaN";

    public static OrderMessage Create(DateTimeOffset now) => new($"msg-poison-{now.ToUnixTimeMilliseconds()}", $"ord-poison-{now.ToUnixTimeMilliseconds()}", Amount, now);
}
