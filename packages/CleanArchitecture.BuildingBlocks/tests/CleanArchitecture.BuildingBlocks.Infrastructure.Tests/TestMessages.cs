using CleanArchitecture.BuildingBlocks.Infrastructure.Messaging;
using CleanArchitecture.BuildingBlocks.Integration;

namespace CleanArchitecture.BuildingBlocks.Infrastructure.Tests;

public enum OrderChannel
{
    WebShop,
    PhoneOrder
}

public sealed record OrderPlaced(Guid OrderId, OrderChannel Channel, string? Note = null);

internal sealed class RecordingHandler : IIntegrationMessageHandler<OrderPlaced>
{
    public List<OrderPlaced> Handled { get; } = [];

    public Func<OrderPlaced, Result> Behavior { get; set; } = _ => Result.Success();

    public Task<Result> HandleAsync(OrderPlaced message, MessageEnvelope envelope, CancellationToken cancellationToken)
    {
        Handled.Add(message);
        return Task.FromResult(Behavior(message));
    }
}

internal sealed class InMemoryInbox : IInboxStore
{
    public HashSet<(string Consumer, Guid MessageId)> Recorded { get; } = [];

    public Task<bool> TryRecordAsync(string consumer, MessageEnvelope envelope, CancellationToken cancellationToken) =>
        Task.FromResult(Recorded.Add((consumer, envelope.MessageId)));
}

internal sealed class CountingUnitOfWork : IUnitOfWork
{
    public int Commits { get; private set; }

    public Task CommitAsync(CancellationToken cancellationToken)
    {
        Commits++;
        return Task.CompletedTask;
    }
}

internal sealed class InMemoryOutboxStore : IOutboxStore
{
    public List<OutboxRecord> Due { get; } = [];

    public List<Guid> Published { get; } = [];

    public List<(Guid MessageId, TimeSpan RetryAfter, string Error)> Failures { get; } = [];

    public Task<IReadOnlyList<OutboxRecord>> ClaimDueAsync(int batchSize, TimeSpan lease, CancellationToken cancellationToken)
    {
        OutboxRecord[] batch = [.. Due.Take(batchSize)];
        Due.RemoveRange(0, batch.Length);
        return Task.FromResult<IReadOnlyList<OutboxRecord>>(batch);
    }

    public Task MarkPublishedAsync(Guid messageId, CancellationToken cancellationToken)
    {
        Published.Add(messageId);
        return Task.CompletedTask;
    }

    public Task MarkFailedAsync(Guid messageId, TimeSpan retryAfter, string error, CancellationToken cancellationToken)
    {
        Failures.Add((messageId, retryAfter, error));
        return Task.CompletedTask;
    }

    public Task<TimeSpan?> GetOldestPendingAgeAsync(CancellationToken cancellationToken) => Task.FromResult<TimeSpan?>(null);
}

internal sealed class RecordingPublisher : IMessagePublisher
{
    public List<(string Stream, MessageEnvelope Envelope)> Sent { get; } = [];

    public Exception? FailWith { get; set; }

    public Task PublishAsync(string stream, MessageEnvelope envelope, CancellationToken cancellationToken)
    {
        if (FailWith is not null)
        {
            throw FailWith;
        }

        Sent.Add((stream, envelope));
        return Task.CompletedTask;
    }
}
