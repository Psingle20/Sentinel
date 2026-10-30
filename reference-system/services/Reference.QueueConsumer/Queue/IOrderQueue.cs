namespace Reference.QueueConsumer.Queue;

/// <summary>
/// The orders queue the consumer works from, in a Redis of its own: a received message stays out of sight until it is
/// completed or returned, every receive is counted, and quarantined messages leave the queue. A queue that cannot be reached throws
/// <see cref="QueueUnavailableException"/>.
/// </summary>
public interface IOrderQueue
{
    const string Name = "orders";

    Task EnqueueAsync(OrderMessage message, CancellationToken cancellationToken);

    /// <summary>Takes the oldest waiting message into flight and counts the receive, or returns null when none waits.</summary>
    Task<ReceivedOrder?> ReceiveAsync(CancellationToken cancellationToken);

    /// <summary>The message was processed: it leaves the queue.</summary>
    Task CompleteAsync(ReceivedOrder received, CancellationToken cancellationToken);

    /// <summary>
    /// Books the order in the ledger and returns how often it has now been booked. Booking is not idempotent: an order
    /// that reached the queue twice is booked twice, which is what S8 exposes.
    /// </summary>
    Task<int> BookAsync(OrderMessage message, CancellationToken cancellationToken);

    /// <summary>Processing failed: the message waits to be received again, unless it was quarantined meanwhile.</summary>
    Task ReturnAsync(ReceivedOrder received, CancellationToken cancellationToken);

    /// <summary>The worker gave up settling a message it holds; the message returns to the queue once it is found abandoned.</summary>
    void Abandon(ReceivedOrder received);

    /// <summary>
    /// Returns messages left in flight that no worker holds, as a visibility timeout returns an abandoned SQS message: a
    /// receive whose reply was lost, or a message whose worker could not settle it.
    /// </summary>
    Task ReclaimAsync(CancellationToken cancellationToken);

    /// <summary>Moves the messages received at least <c>MinimumReceives</c> times to the quarantine, at most <c>MaxMessages</c>.</summary>
    Task<QuarantineResult> QuarantineAsync(QuarantineRequest request, CancellationToken cancellationToken);

    /// <summary>Returns every quarantined message to the queue: the quarantine's compensation.</summary>
    Task<QuarantineResult> ReleaseAsync(CancellationToken cancellationToken);

    /// <summary>Empties the queue entirely, for a scenario reset.</summary>
    Task ResetAsync(CancellationToken cancellationToken);

    Task<QueueStatus> StatusAsync(CancellationToken cancellationToken);

    /// <summary>One message's state: waiting, in flight, quarantined, or unknown once it was processed or never existed.</summary>
    Task<MessageView> MessageAsync(string id, CancellationToken cancellationToken);
}
