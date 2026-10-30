using Reference.QueueConsumer.Queue;
using Shouldly;

namespace Reference.Tests;

/// <summary>
/// The Redis orders queue as SQS would behave: a message whose worker abandoned it, by a lost reply or a settle that
/// kept failing, returns to the front of the queue once it has been found unheld at two samples, and counts as
/// redelivered; a message a worker still holds, however long, never returns.
/// </summary>
[Collection(RedisCollection.Name)]
public sealed class OrderQueueTests(SmallRedis redis)
{
    [Fact]
    public async Task An_abandoned_message_returns_after_two_samples_and_a_held_one_never_does()
    {
        await using var queue = new RedisOrderQueue(redis.Endpoint, TimeProvider.System);
        await queue.ResetAsync(CancellationToken.None);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        await queue.EnqueueAsync(new OrderMessage("msg-1", "ord-1", "10.00", now), CancellationToken.None);
        await queue.EnqueueAsync(new OrderMessage("msg-2", "ord-2", "10.00", now), CancellationToken.None);

        ReceivedOrder abandoned = (await queue.ReceiveAsync(CancellationToken.None)).ShouldNotBeNull();
        ReceivedOrder held = (await queue.ReceiveAsync(CancellationToken.None)).ShouldNotBeNull();
        queue.Abandon(abandoned);

        await queue.ReclaimAsync(CancellationToken.None);
        (await queue.StatusAsync(CancellationToken.None)).InFlight.ShouldBe(2, "one sample is not enough to call a message abandoned");

        await queue.ReclaimAsync(CancellationToken.None);
        QueueStatus status = await queue.StatusAsync(CancellationToken.None);
        status.InFlight.ShouldBe(1, "the held message stays in flight");
        status.Waiting.ShouldBe(1);

        ReceivedOrder again = (await queue.ReceiveAsync(CancellationToken.None)).ShouldNotBeNull();
        again.Message.Id.ShouldBe(abandoned.Message.Id, "a reclaimed message goes to the front of the queue");
        again.Receive.ShouldBe(2, "a reclaimed message counts as redelivered");

        await queue.CompleteAsync(held, CancellationToken.None);
        await queue.CompleteAsync(again, CancellationToken.None);
        await queue.ReclaimAsync(CancellationToken.None);
        await queue.ReclaimAsync(CancellationToken.None);
        (await queue.StatusAsync(CancellationToken.None)).InFlight.ShouldBe(0);
    }
}
