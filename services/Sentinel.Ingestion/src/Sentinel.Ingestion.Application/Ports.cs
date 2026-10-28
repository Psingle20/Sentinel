using Sentinel.Contracts.Observations;
using Sentinel.Contracts.Queries;
using Sentinel.Ingestion.Domain.Sources;

namespace Sentinel.Ingestion.Application;

public sealed record ObservationPage(IReadOnlyList<Observation> Observations, bool Truncated, string? ContinuationToken);

/// <summary>Ingestion's observation store: the source of query answers and of telemetry replay.</summary>
public interface IObservationStore
{
    /// <summary>
    /// Adds observations inside the current unit of work and returns the ones that were new;
    /// an observation whose ID is already stored is a duplicate and is skipped.
    /// </summary>
    Task<IReadOnlyList<Observation>> AddNewAsync(IReadOnlyList<Observation> observations, CancellationToken cancellationToken);

    Task<ObservationPage> QueryAsync(ObservationQuery query, CancellationToken cancellationToken);
}

public interface ISourceStateStore
{
    Task<SourceState?> GetAsync(string sourceId, CancellationToken cancellationToken);

    Task<IReadOnlyList<SourceState>> GetAllAsync(CancellationToken cancellationToken);

    /// <summary>Saves inside the current unit of work, so a checkpoint moves only with its observations.</summary>
    Task SaveAsync(SourceState state, CancellationToken cancellationToken);
}

/// <summary>Ingestion's operational measurements per source: throughput, duplicates, late data and watermark lag (Phase 02).</summary>
public interface IIngestionMetrics
{
    void Pulled(string sourceId, int added, int duplicates, int late, TimeSpan watermarkLag);

    void SourceFailed(string sourceId, bool unavailable);
}
