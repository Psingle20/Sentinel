namespace Sentinel.Gateway.Application.Idempotency;

/// <summary>A mutation's first response, kept so a retry with the same key gets it again instead of a second change.</summary>
public sealed record StoredResponse(int StatusCode, string? ContentType, string Body);

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

/// <summary>
/// Idempotency keys per caller (Phase 08 API principles). A key is reserved before the mutation runs,
/// so two concurrent retries cannot both change state, and released if the mutation fails on the
/// server, so the client can retry. Keys expire after a day and can then be reused.
/// </summary>
public interface IIdempotencyStore
{
    Task<IdempotencyClaim> ClaimAsync(string actor, string key, string requestHash, DateTimeOffset now, CancellationToken cancellationToken);

    Task CompleteAsync(string actor, string key, StoredResponse response, DateTimeOffset now, CancellationToken cancellationToken);

    Task ReleaseAsync(string actor, string key, CancellationToken cancellationToken);
}
