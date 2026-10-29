using Sentinel.Contracts.Observations;
using Sentinel.Contracts.Queries;
using Sentinel.Ingestion.Domain.Sources;

namespace Sentinel.Ingestion.Application.Abstractions.Diagnostics;

/// <summary>Ingestion's operational measurements per source: throughput, duplicates, late data and watermark lag (Phase 02).</summary>
public interface IIngestionMetrics
{
    void Pulled(string sourceId, int added, int duplicates, int late, TimeSpan watermarkLag);

    void SourceFailed(string sourceId, bool unavailable);
}
