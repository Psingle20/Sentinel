using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using Reference.Common.Redis;
using Reference.Common.Revisions;
using Reference.Common.Telemetry;

namespace Reference.Checkout.Revisions;

public static class CheckoutRevisions
{
    public const string ServiceName = "checkout-service";
    public const string Baseline = "v2.4.0";

    /// <summary>S1's cause: warms each session on a dedicated Redis connection that is never returned.</summary>
    public const string ConnectionLeak = "v2.4.1";

    public static readonly IReadOnlyCollection<string> All = [Baseline, ConnectionLeak, "v2.4.2", "v2.4.3"];
}
