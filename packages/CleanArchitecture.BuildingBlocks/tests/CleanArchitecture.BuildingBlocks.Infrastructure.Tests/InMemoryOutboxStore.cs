using CleanArchitecture.BuildingBlocks.Infrastructure.Messaging;
using CleanArchitecture.BuildingBlocks.Integration;

namespace CleanArchitecture.BuildingBlocks.Infrastructure.Tests;

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
