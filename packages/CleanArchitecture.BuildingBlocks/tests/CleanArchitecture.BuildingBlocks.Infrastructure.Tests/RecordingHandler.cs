using CleanArchitecture.BuildingBlocks.Infrastructure.Messaging;
using CleanArchitecture.BuildingBlocks.Integration;

namespace CleanArchitecture.BuildingBlocks.Infrastructure.Tests;

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
