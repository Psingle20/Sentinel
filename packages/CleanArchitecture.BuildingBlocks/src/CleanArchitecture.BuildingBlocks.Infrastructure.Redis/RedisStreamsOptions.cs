namespace CleanArchitecture.BuildingBlocks.Infrastructure.Redis;

public sealed class RedisStreamsOptions
{
    /// <summary>Approximate maximum entries kept per stream; older entries are trimmed on publish.</summary>
    public int MaxStreamLength { get; set; } = 100_000;

    /// <summary>Delay between polls when a stream has no new or reclaimable entries.</summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromMilliseconds(200);

    public int BatchSize { get; set; } = 20;

    /// <summary>How long an entry may stay unacknowledged before another consumer reclaims it.</summary>
    public TimeSpan ClaimIdleTime { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Deliveries after which a failing entry moves to the dead-letter stream.</summary>
    public int MaxDeliveries { get; set; } = 5;

    /// <summary>This instance's consumer name within its consumer group.</summary>
    public string ConsumerName { get; set; } = $"{Environment.MachineName}-{Environment.ProcessId}";

    internal bool IsValid() =>
        MaxStreamLength > 0
        && PollInterval > TimeSpan.Zero
        && BatchSize > 0
        && ClaimIdleTime > TimeSpan.Zero
        && MaxDeliveries > 0
        && !string.IsNullOrWhiteSpace(ConsumerName);
}
