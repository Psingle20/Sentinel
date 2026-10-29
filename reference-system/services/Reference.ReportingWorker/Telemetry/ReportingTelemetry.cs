using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using Reference.Common.Redis;
using Reference.ReportingWorker.Batch;

namespace Reference.ReportingWorker.Telemetry;

public sealed class ReportingTelemetry : IDisposable
{
    public const string SourceName = "Reference.ReportingWorker";

    private readonly Meter _meter = new(SourceName);

    public ReportingTelemetry(ReportingBatch batch)
    {
        ArgumentNullException.ThrowIfNull(batch);

        _meter.CreateObservableGauge("reference.reporting.batch.active", () => batch.Active ? 1 : 0, "{batch}", "1 while a reporting batch runs.");
        _meter.CreateObservableGauge("reference.reporting.redis.connections", () => batch.Connections, "{connection}", "Redis connections held by the batch.");
    }

    public void Dispose() => _meter.Dispose();
}
