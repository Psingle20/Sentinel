namespace CleanArchitecture.BuildingBlocks.Integration;

/// <summary>
/// Optional metadata for an outgoing message. Values left null are taken from the message
/// being handled, if any: its correlation ID, its message ID as causation, and its headers.
/// </summary>
public sealed record MessageMetadata(
    string? CorrelationId = null,
    string? CausationId = null,
    IReadOnlyDictionary<string, string>? Headers = null);
