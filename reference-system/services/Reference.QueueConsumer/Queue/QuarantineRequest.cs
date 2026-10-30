namespace Reference.QueueConsumer.Queue;

/// <summary>Quarantine the messages received at least <see cref="MinimumReceives"/> times, at most <see cref="MaxMessages"/> of them.</summary>
public sealed record QuarantineRequest(int MinimumReceives = 3, int MaxMessages = 5);
