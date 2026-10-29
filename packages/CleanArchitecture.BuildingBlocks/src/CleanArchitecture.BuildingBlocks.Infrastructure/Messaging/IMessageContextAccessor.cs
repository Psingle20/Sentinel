namespace CleanArchitecture.BuildingBlocks.Infrastructure.Messaging;

/// <summary>The envelope of the message being handled in the current scope, if any.</summary>
public interface IMessageContextAccessor
{
    MessageEnvelope? Current { get; }
}
