namespace CleanArchitecture.BuildingBlocks.Infrastructure.Messaging;

/// <summary>
/// The wire representation of every integration message. <see cref="Payload"/> is the
/// contract serialized as JSON; everything else is transport-independent metadata.
/// </summary>
public sealed record MessageEnvelope(
    Guid MessageId,
    string MessageType,
    int SchemaVersion,
    DateTimeOffset OccurredAt,
    string Producer,
    string? CorrelationId,
    string? CausationId,
    IReadOnlyDictionary<string, string> Headers,
    string Payload);
