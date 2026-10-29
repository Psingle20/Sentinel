namespace CleanArchitecture.BuildingBlocks.Infrastructure.Messaging;

/// <summary>Handles one integration message type. Return a failure to have the delivery retried.</summary>
public interface IIntegrationMessageHandler<in TMessage>
    where TMessage : class
{
    Task<Result> HandleAsync(TMessage message, MessageEnvelope envelope, CancellationToken cancellationToken);
}
