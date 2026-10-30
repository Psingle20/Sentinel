namespace Reference.QueueConsumer.Queue;

/// <summary>One order message as it sits in the queue. The amount is text, as an upstream producer sends it.</summary>
public sealed record OrderMessage(string Id, string Amount, DateTimeOffset EnqueuedAt);
