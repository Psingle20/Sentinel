namespace Reference.Orders.Faults;

/// <summary>
/// The injected cause of S7: order confirmations slow by 20 ms for every second the fault lasts, up to 2 s, and each
/// one runs to the end even after its caller has given up, as a store that never sees the client leave would. Nothing
/// about order-service's revision changes.
/// </summary>
public sealed class SlowConfirmations(TimeProvider time)
{
    public static readonly TimeSpan BaseLatency = TimeSpan.FromMilliseconds(20);

    /// <summary>Added per second that the fault has lasted.</summary>
    public static readonly TimeSpan GrowthPerSecond = TimeSpan.FromMilliseconds(20);

    public static readonly TimeSpan MaxLatency = TimeSpan.FromSeconds(2);

    private long _sinceTicks;
    private long _untilTicks;

    public bool Active => time.GetUtcNow().UtcTicks < Interlocked.Read(ref _untilTicks);

    /// <summary>How long a confirmation takes now.</summary>
    public TimeSpan Latency()
    {
        DateTimeOffset now = time.GetUtcNow();
        if (now.UtcTicks >= Interlocked.Read(ref _untilTicks))
        {
            return BaseLatency;
        }

        TimeSpan grown = BaseLatency + GrowthPerSecond * (now.UtcTicks - Interlocked.Read(ref _sinceTicks)) / TimeSpan.TicksPerSecond;
        return grown < MaxLatency ? grown : MaxLatency;
    }

    public void Start(TimeSpan duration)
    {
        DateTimeOffset now = time.GetUtcNow();
        Interlocked.Exchange(ref _sinceTicks, now.UtcTicks);
        Interlocked.Exchange(ref _untilTicks, (now + duration).UtcTicks);
    }

    public void Stop() => Interlocked.Exchange(ref _untilTicks, 0);
}
