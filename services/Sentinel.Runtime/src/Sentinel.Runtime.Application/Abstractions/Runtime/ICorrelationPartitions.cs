using CleanArchitecture.BuildingBlocks;
using Sentinel.Contracts.Incidents;
using Sentinel.Contracts.Observations;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Contracts.Queries;
using Sentinel.Contracts.Remediation;
using Sentinel.Runtime.Application.Incidents;
using Sentinel.Runtime.Domain.Correlation;
using Sentinel.Runtime.Domain.Incidents;

namespace Sentinel.Runtime.Application.Abstractions.Runtime;

/// <summary>
/// Serializes correlation for one tenant and environment (CorrelationGrain in a running Runtime),
/// so two signals for the same failure can never open two incidents.
/// </summary>
public interface ICorrelationPartitions
{
    Task ObserveAsync(Observation observation, CancellationToken cancellationToken);
}
