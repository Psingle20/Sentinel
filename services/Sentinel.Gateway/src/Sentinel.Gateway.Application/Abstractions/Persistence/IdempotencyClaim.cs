namespace Sentinel.Gateway.Application.Abstractions.Persistence;

/// <summary>What claiming an idempotency key found.</summary>
public abstract record IdempotencyClaim
{
    private IdempotencyClaim()
    {
    }

    /// <summary>The key is new (or expired): this request runs and its response is recorded.</summary>
    public sealed record Claimed : IdempotencyClaim;

    /// <summary>The same request finished before: answer with its response.</summary>
    public sealed record Replay(StoredResponse Response) : IdempotencyClaim;

    /// <summary>The key was used for a different request.</summary>
    public sealed record Mismatch : IdempotencyClaim;

    /// <summary>The same request is still running.</summary>
    public sealed record InProgress : IdempotencyClaim;
}
