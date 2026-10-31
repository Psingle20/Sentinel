using Reference.Orders.Revisions;

namespace Reference.Orders.Storage;

/// <summary>
/// The S3 fault: v1.8.0's order lookup slows by 4 ms for every second it serves, up to 2 s, so
/// checkout's timeouts start about two minutes after it begins serving. The S10 fault: v1.8.1's cache
/// misses two lookups in five from the moment it serves, and a miss outlasts checkout's attempt timeout, so
/// about one checkout in sixteen fails all three attempts: fewer than a Redis leak fails, but enough to burn the SLO.
/// </summary>
public static class SlowOrderLookup
{
    public static readonly TimeSpan BaseQueryLatency = TimeSpan.FromMilliseconds(30);

    /// <summary>Added per second that v1.8.0 has been serving.</summary>
    public static readonly TimeSpan GrowthPerSecond = TimeSpan.FromMilliseconds(4);

    public static readonly TimeSpan MaxQueryLatency = TimeSpan.FromSeconds(2);

    /// <summary>The share of v1.8.1's lookups that miss its cache.</summary>
    public const double CacheMissShare = 0.4;

    /// <summary>A v1.8.1 lookup that missed its cache and read the orders table instead.</summary>
    public static readonly TimeSpan CacheMissLatency = TimeSpan.FromMilliseconds(900);

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

    /// <summary>One lookup's latency before jitter; <paramref name="draw"/>, in [0, 1), decides whether a v1.8.1 lookup misses its cache.</summary>
    public static TimeSpan Latency(string revision, TimeSpan uptime, double draw) =>
        revision == OrderRevisions.ColdCache && draw < CacheMissShare ? CacheMissLatency : Latency(revision, uptime);
}
