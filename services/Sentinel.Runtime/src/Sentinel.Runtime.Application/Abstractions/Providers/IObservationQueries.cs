using CleanArchitecture.BuildingBlocks;
using Sentinel.Contracts.Incidents;
using Sentinel.Contracts.Observations;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Contracts.Queries;
using Sentinel.Contracts.Remediation;
using Sentinel.Runtime.Application.Incidents;
using Sentinel.Runtime.Domain.Correlation;
using Sentinel.Runtime.Domain.Incidents;

namespace Sentinel.Runtime.Application.Abstractions.Providers;

/// <summary>Ingestion's observation query contract (ADR-007).</summary>
public interface IObservationQueries
{
    Task<ObservationQueryResult> QueryAsync(ObservationQuery query, CancellationToken cancellationToken);
}
