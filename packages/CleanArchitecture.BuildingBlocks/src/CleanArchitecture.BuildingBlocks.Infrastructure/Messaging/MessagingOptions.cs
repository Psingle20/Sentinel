namespace CleanArchitecture.BuildingBlocks.Infrastructure.Messaging;

public sealed class MessagingOptions
{
    /// <summary>The producing service's name in envelopes, and its consumer name in inboxes and consumer groups.</summary>
    public string ServiceName { get; set; } = string.Empty;
}

public sealed class OutboxOptions
{
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(1);

    public int BatchSize { get; set; } = 50;

    /// <summary>How long a claimed record stays invisible to other dispatchers while it is published.</summary>
    public TimeSpan ClaimLease { get; set; } = TimeSpan.FromSeconds(30);

    public TimeSpan InitialRetryDelay { get; set; } = TimeSpan.FromSeconds(1);

    public TimeSpan MaxRetryDelay { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>The outbox health check reports degraded when the oldest unpublished record is older than this.</summary>
    public TimeSpan LagThreshold { get; set; } = TimeSpan.FromMinutes(1);

    internal bool IsValid() =>
        PollInterval > TimeSpan.Zero
        && BatchSize > 0
        && ClaimLease > TimeSpan.Zero
        && InitialRetryDelay > TimeSpan.Zero
        && MaxRetryDelay >= InitialRetryDelay
        && LagThreshold > TimeSpan.Zero;
}
