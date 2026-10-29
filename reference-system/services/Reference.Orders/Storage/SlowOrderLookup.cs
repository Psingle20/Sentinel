using Reference.Orders.Revisions;

namespace Reference.Orders.Storage;

/// <summary>
/// The S3 fault: v1.8.0's order lookup slows by 4 ms for every second it serves, up to 2 s, so
/// checkout's timeouts start about two minutes after it begins serving.
/// </summary>
public static class SlowOrderLookup
{
    public static readonly TimeSpan BaseQueryLatency = TimeSpan.FromMilliseconds(30);

    /// <summary>Added per second that v1.8.0 has been serving.</summary>
    public static readonly TimeSpan GrowthPerSecond = TimeSpan.FromMilliseconds(4);

    public static readonly TimeSpan MaxQueryLatency = TimeSpan.FromSeconds(2);

    /// <summary>The lookup latency <paramref name="revision"/> produces after serving for <paramref name="uptime"/>, before jitter.</summary>
    public static TimeSpan Latency(string revision, TimeSpan uptime)
    {
        if (revision != OrderRevisions.SlowQuery)
        {
            return BaseQueryLatency;
        }

        TimeSpan grown = BaseQueryLatency + GrowthPerSecond * uptime.TotalSeconds;
        return grown < MaxQueryLatency ? grown : MaxQueryLatency;
    }
}
