using System.Globalization;
using StackExchange.Redis;

namespace CleanArchitecture.BuildingBlocks.Infrastructure.Redis;

/// <summary>One dead-lettered entry: the message and why its consumer gave up on it.</summary>
public sealed record DeadLetter(
    string EntryId,
    string Stream,
    Guid? MessageId,
    string? MessageType,
    string Reason,
    string ConsumerGroup,
    string SourceEntryId,
    DateTimeOffset? FailedAt);
