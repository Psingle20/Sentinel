using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using Reference.Common.Redis;

namespace Reference.ReportingWorker.Batch;

public sealed record BatchStatus(bool Active, int Connections, int TargetConnections, DateTimeOffset? StartedAt, string? LastError);
