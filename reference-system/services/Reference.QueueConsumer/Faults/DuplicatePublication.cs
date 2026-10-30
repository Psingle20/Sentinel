namespace Reference.QueueConsumer.Faults;

/// <summary>
/// The injected cause of S8: for a while, the upstream retries its publishes and half of the orders reach the queue
/// twice, as at-least-once delivery allows. Nothing about the consumer changes; its booking was never idempotent, and
/// now that shows.
/// </summary>
public sealed class DuplicatePublication(TimeProvider time)
{
    public const double Share = 0.5;

    private long _untilTicks;

    public bool Active => time.GetUtcNow().UtcTicks < Interlocked.Read(ref _untilTicks);

    public void Start(TimeSpan duration) => Interlocked.Exchange(ref _untilTicks, (time.GetUtcNow() + duration).UtcTicks);

    public void Stop() => Interlocked.Exchange(ref _untilTicks, 0);
}
