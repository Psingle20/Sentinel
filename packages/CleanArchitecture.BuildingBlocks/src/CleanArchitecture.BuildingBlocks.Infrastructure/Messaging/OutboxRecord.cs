namespace CleanArchitecture.BuildingBlocks.Infrastructure.Messaging;

/// <summary>An outbox message waiting to be published to <see cref="Stream"/>.</summary>
public sealed record OutboxRecord(MessageEnvelope Envelope, string Stream, int Attempts = 0);
