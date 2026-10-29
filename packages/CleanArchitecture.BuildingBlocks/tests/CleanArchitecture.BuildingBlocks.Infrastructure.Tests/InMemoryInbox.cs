using CleanArchitecture.BuildingBlocks.Infrastructure.Messaging;
using CleanArchitecture.BuildingBlocks.Integration;

namespace CleanArchitecture.BuildingBlocks.Infrastructure.Tests;

internal sealed class InMemoryInbox : IInboxStore
{
    public HashSet<(string Consumer, Guid MessageId)> Recorded { get; } = [];

    public Task<bool> TryRecordAsync(string consumer, MessageEnvelope envelope, CancellationToken cancellationToken) =>
        Task.FromResult(Recorded.Add((consumer, envelope.MessageId)));
}
