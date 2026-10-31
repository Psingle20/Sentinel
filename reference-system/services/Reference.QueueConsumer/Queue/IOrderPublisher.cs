namespace Reference.QueueConsumer.Queue;

/// <summary>How the upstream producer hands an order on: straight to the orders queue, or through an event route to it.</summary>
public interface IOrderPublisher
{
    /// <summary>The route the producer publishes on, as its telemetry labels it.</summary>
    string Route { get; }

    /// <exception cref="QueueUnavailableException">The queue or the route did not accept the order.</exception>
    Task PublishAsync(OrderMessage order, CancellationToken cancellationToken);
}
