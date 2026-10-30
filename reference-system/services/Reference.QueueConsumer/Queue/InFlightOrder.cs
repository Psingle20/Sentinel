namespace Reference.QueueConsumer.Queue;

/// <summary>A message this consumer holds from SQS: its ID, when it was first sent and which receive this is.</summary>
public sealed record InFlightOrder(string Id, DateTimeOffset SentAt, int Receive);
