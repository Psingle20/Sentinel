namespace Sentinel.Contracts.Remediation;

/// <summary>
/// Quarantine a consumer's poison messages: the messages of <see cref="Queue"/> received at least
/// <see cref="MinimumReceives"/> times, at most <see cref="MaxMessages"/> of them, leave the queue for a quarantine where
/// they wait for a person, as a redrive policy moves them to a dead-letter queue (Phase E01). Compensated by releasing
/// them back to the queue.
/// </summary>
public sealed record QuarantineMessageAction : RemediationAction
{
    public required string Queue { get; init; }

    public required int MinimumReceives { get; init; }

    public required int MaxMessages { get; init; }

    /// <summary>Why the messages are quarantined, for the person who must decide what becomes of them.</summary>
    public required string Reason { get; init; }
}
