namespace CleanArchitecture.BuildingBlocks.Infrastructure.Messaging;

/// <summary>
/// Durable outbox storage used by the dispatcher. Adding records happens inside the store's
/// unit of work, so it is not part of this interface.
/// </summary>
public interface IOutboxStore
{
    /// <summary>
    /// Claims up to <paramref name="batchSize"/> due records for <paramref name="lease"/>, so
    /// concurrent dispatchers do not publish the same record at the same time.
    /// </summary>
    Task<IReadOnlyList<OutboxRecord>> ClaimDueAsync(int batchSize, TimeSpan lease, CancellationToken cancellationToken);

    Task MarkPublishedAsync(Guid messageId, CancellationToken cancellationToken);

    /// <summary>Records a failed attempt; the record becomes due again after <paramref name="retryAfter"/>.</summary>
    Task MarkFailedAsync(Guid messageId, TimeSpan retryAfter, string error, CancellationToken cancellationToken);

    /// <summary>Age of the oldest unpublished record, or null when the outbox is empty.</summary>
    Task<TimeSpan?> GetOldestPendingAgeAsync(CancellationToken cancellationToken);
}
