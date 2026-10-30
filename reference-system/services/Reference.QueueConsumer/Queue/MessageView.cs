namespace Reference.QueueConsumer.Queue;

/// <summary>A message that has been received more than once: its ID, how often, and whether it waits, is in flight or is quarantined.</summary>
public sealed record MessageView(string Id, int Receives, string State);
