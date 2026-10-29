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

    public static readonly IReadOnlyCollection<string> All = [Baseline, "v1.7.1", SlowQuery];
}
