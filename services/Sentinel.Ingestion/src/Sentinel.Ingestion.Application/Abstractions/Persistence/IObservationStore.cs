using Sentinel.Contracts.Observations;
using Sentinel.Contracts.Queries;
using Sentinel.Ingestion.Domain.Sources;

namespace Sentinel.Ingestion.Application.Abstractions.Persistence;

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
