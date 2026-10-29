namespace Sentinel.Contracts;

/// <summary>An event-time interval. <see cref="To"/> is exclusive.</summary>
public sealed record TimeRange(DateTimeOffset From, DateTimeOffset To);
