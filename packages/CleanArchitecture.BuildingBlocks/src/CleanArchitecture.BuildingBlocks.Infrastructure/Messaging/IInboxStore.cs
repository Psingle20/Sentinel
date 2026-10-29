namespace CleanArchitecture.BuildingBlocks.Infrastructure.Messaging;

/// <summary>Records handled messages per consumer, inside the current unit of work.</summary>
public interface IInboxStore
{
    /// <summary>Returns false when <paramref name="consumer"/> has already handled the message.</summary>
    Task<bool> TryRecordAsync(string consumer, MessageEnvelope envelope, CancellationToken cancellationToken);
}
