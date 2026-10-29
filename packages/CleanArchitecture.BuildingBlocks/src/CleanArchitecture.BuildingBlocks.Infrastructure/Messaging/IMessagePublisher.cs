namespace CleanArchitecture.BuildingBlocks.Infrastructure.Messaging;

/// <summary>Sends envelopes to a transport. Implemented by a transport package, such as Redis Streams.</summary>
public interface IMessagePublisher
{
    Task PublishAsync(string stream, MessageEnvelope envelope, CancellationToken cancellationToken);
}
