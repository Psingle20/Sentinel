namespace CleanArchitecture.BuildingBlocks.Infrastructure.Messaging;

internal sealed class MessageContextAccessor : IMessageContextAccessor
{
    public MessageEnvelope? Current { get; set; }
}
