namespace CleanArchitecture.BuildingBlocks.Infrastructure.Messaging;

/// <summary>An outbox message waiting to be published to <see cref="Stream"/>.</summary>
public sealed record OutboxRecord(MessageEnvelope Envelope, string Stream, int Attempts = 0);

/// <summary>Sends envelopes to a transport. Implemented by a transport package, such as Redis Streams.</summary>
public interface IMessagePublisher
{
    Task PublishAsync(string stream, MessageEnvelope envelope, CancellationToken cancellationToken);
}

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

/// <summary>Records handled messages per consumer, inside the current unit of work.</summary>
public interface IInboxStore
{
    /// <summary>Returns false when <paramref name="consumer"/> has already handled the message.</summary>
    Task<bool> TryRecordAsync(string consumer, MessageEnvelope envelope, CancellationToken cancellationToken);
}

/// <summary>Prepares storage, such as creating tables, before the dispatcher and consumers start.</summary>
public interface IMessagingStoreInitializer
{
    Task InitializeAsync(CancellationToken cancellationToken);
}

/// <summary>Handles one integration message type. Return a failure to have the delivery retried.</summary>
public interface IIntegrationMessageHandler<in TMessage>
    where TMessage : class
{
    Task<Result> HandleAsync(TMessage message, MessageEnvelope envelope, CancellationToken cancellationToken);
}

/// <summary>The envelope of the message being handled in the current scope, if any.</summary>
public interface IMessageContextAccessor
{
    MessageEnvelope? Current { get; }
}

internal sealed class MessageContextAccessor : IMessageContextAccessor
{
    public MessageEnvelope? Current { get; set; }
}

/// <summary>Gives a unit-of-work implementation the messages enqueued in its scope.</summary>
public interface IPendingOutbox
{
    IReadOnlyList<OutboxRecord> DequeuePending();
}
