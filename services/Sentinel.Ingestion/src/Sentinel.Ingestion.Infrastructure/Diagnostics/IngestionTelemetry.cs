using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sentinel.Ingestion.Application.Abstractions.Diagnostics;

namespace Sentinel.Ingestion.Infrastructure.Diagnostics;

/// <summary>
/// Ingestion's own telemetry for the Phase 02 operational dashboard: throughput, duplicates, late
/// data, watermark lag, failures and pull latency per source. The Aspire dashboard shows it locally.
/// </summary>
internal sealed class IngestionTelemetry : IIngestionMetrics, IDisposable
{
    public const string MeterName = "Sentinel.Ingestion";

    private readonly Meter _meter = new(MeterName);

    public IngestionTelemetry()
    {
        Ingested = _meter.CreateCounter<long>("sentinel.ingestion.observations", "{observation}", "New observations stored and published, per source.");
        PullFailures = _meter.CreateCounter<long>("sentinel.ingestion.pull.failures", "{failure}", "Pulls that threw, per source.");
        PullDuration = _meter.CreateHistogram("sentinel.ingestion.pull.duration", "s", "Time to pull and commit one source.", tags: null,
            new InstrumentAdvice<double> { HistogramBucketBoundaries = [0.01, 0.025, 0.05, 0.1, 0.25, 0.5, 1, 2.5, 5, 10] });
        Duplicates = _meter.CreateCounter<long>("sentinel.ingestion.duplicates", "{observation}", "Observations a pull returned that were already stored, per source.");
        Late = _meter.CreateCounter<long>("sentinel.ingestion.late", "{observation}", "New observations older than the source's watermark (ADR-016), per source.");
        WatermarkLag = _meter.CreateHistogram("sentinel.ingestion.watermark.lag", "s", "How far a source's watermark trails now after a pull.", tags: null,
            new InstrumentAdvice<double> { HistogramBucketBoundaries = [5, 10, 15, 30, 60, 120, 300, 600] });
        SourceFailures = _meter.CreateCounter<long>("sentinel.ingestion.source.failures", "{failure}", "Provider calls that returned an outage, per source.");
    }

    public Counter<long> Ingested { get; }

    public Counter<long> PullFailures { get; }

    public Histogram<double> PullDuration { get; }

    public Counter<long> Duplicates { get; }

    public Counter<long> Late { get; }

    public Histogram<double> WatermarkLag { get; }

    public Counter<long> SourceFailures { get; }

    public void Pulled(string sourceId, int added, int duplicates, int late, TimeSpan watermarkLag)
    {
        var source = new KeyValuePair<string, object?>("source", sourceId);
        Duplicates.Add(duplicates, source);
        Late.Add(late, source);
        WatermarkLag.Record(watermarkLag.TotalSeconds, source);
    }

    public void SourceFailed(string sourceId, bool unavailable) =>
        SourceFailures.Add(1, new KeyValuePair<string, object?>("source", sourceId), new KeyValuePair<string, object?>("unavailable", unavailable));

    public void Dispose() => _meter.Dispose();
}
