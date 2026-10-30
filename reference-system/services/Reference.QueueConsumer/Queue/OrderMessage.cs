namespace Reference.QueueConsumer.Queue;

/// <summary>
/// One order message as it sits in the queue: its own message ID, the order it carries, and the amount as text, as an
/// upstream producer sends it. An order published twice travels in two messages with the same order ID.
/// </summary>
public sealed record OrderMessage(string Id, string OrderId, string Amount, DateTimeOffset EnqueuedAt);
