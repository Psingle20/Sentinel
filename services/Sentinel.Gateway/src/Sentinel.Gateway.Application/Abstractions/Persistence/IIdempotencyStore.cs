namespace Sentinel.Gateway.Application.Abstractions.Persistence;

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
