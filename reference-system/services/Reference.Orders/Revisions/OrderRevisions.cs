using System.Diagnostics;
using System.Diagnostics.Metrics;
using Reference.Common;
using Reference.Common.Redis;
using Reference.Common.Revisions;
using Reference.Common.Telemetry;

namespace Reference.Orders.Revisions;

public static class OrderRevisions
{
    public const string ServiceName = "order-service";
    public const string Baseline = "v1.7.0";

    /// <summary>S3's cause: an unindexed lookup whose latency grows with every second the revision serves.</summary>
    public const string SlowQuery = "v1.8.0";

    /// <summary>S10's second cause: an order cache that misses two lookups in five, each miss slower than checkout's attempt timeout.</summary>
    public const string ColdCache = "v1.8.1";

    /// <summary>S11's confounder: a configuration that widens the Redis pool from 4 to 40 connections, which then hold steady.</summary>
    public const string WidePool = "v1.7.2";

    public static readonly IReadOnlyCollection<string> All = [Baseline, "v1.7.1", WidePool, SlowQuery, ColdCache];
}
