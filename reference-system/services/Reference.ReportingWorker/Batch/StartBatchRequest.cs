using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using Reference.Common.Redis;

namespace Reference.ReportingWorker.Batch;

/// <summary>Batch parameters. The defaults bring Redis close to maxclients in about a minute.</summary>
public sealed record StartBatchRequest(int TargetConnections = 190, double ConnectionsPerSecond = 3);
