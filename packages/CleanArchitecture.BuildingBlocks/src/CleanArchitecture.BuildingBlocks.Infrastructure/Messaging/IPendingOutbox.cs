namespace CleanArchitecture.BuildingBlocks.Infrastructure.Messaging;

/// <summary>Gives a unit-of-work implementation the messages enqueued in its scope.</summary>
public interface IPendingOutbox
{
    IReadOnlyList<OutboxRecord> DequeuePending();
}
