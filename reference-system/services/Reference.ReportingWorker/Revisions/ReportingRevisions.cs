using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using Reference.Common.Redis;

namespace Reference.ReportingWorker.Revisions;

public static class ReportingRevisions
{
    public const string ServiceName = "reporting-worker";
    public const string Baseline = "v3.1.0";

    public static readonly IReadOnlyCollection<string> All = [Baseline];
}
