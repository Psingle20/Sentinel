namespace Reference.QueueConsumer.Queue;

/// <summary>The messages a quarantine or release moved, by ID.</summary>
public sealed record QuarantineResult(IReadOnlyList<string> Messages);
