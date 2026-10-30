namespace Reference.QueueConsumer.Queue;

/// <summary>
/// A message in flight: the order it carries, the handle that completes or returns it (the raw entry in Redis, the
/// receipt handle in SQS), and which receive of the message this is.
/// </summary>
public sealed record ReceivedOrder(OrderMessage Message, string Handle, int Receive);
