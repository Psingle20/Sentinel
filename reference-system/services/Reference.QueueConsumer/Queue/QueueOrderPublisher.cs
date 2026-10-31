namespace Reference.QueueConsumer.Queue;

/// <summary>Publishes an order straight onto the orders queue, as the local reference environment does.</summary>
public sealed class QueueOrderPublisher(IOrderQueue queue) : IOrderPublisher
{
    public string Route => "queue";

    public Task PublishAsync(OrderMessage order, CancellationToken cancellationToken) => queue.EnqueueAsync(order, cancellationToken);
}
