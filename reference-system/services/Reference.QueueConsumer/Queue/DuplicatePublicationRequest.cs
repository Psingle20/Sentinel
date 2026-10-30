namespace Reference.QueueConsumer.Queue;

/// <summary>Publish every other order twice for <see cref="Seconds"/>.</summary>
public sealed record DuplicatePublicationRequest(int Seconds = 120);
