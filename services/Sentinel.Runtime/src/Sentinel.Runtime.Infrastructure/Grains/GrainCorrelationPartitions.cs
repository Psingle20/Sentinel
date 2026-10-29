using CleanArchitecture.BuildingBlocks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sentinel.Contracts.Observations;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Runtime.Application;
using Sentinel.Runtime.Application.Incidents;
using Sentinel.Runtime.Application.Abstractions.Runtime;

namespace Sentinel.Runtime.Infrastructure.Grains;

/// <summary><see cref="ICorrelationPartitions"/> backed by CorrelationGrain.</summary>
internal sealed class GrainCorrelationPartitions(IGrainFactory grains) : ICorrelationPartitions
{
    public Task ObserveAsync(Observation observation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(observation);

        return grains.GetGrain<ICorrelationGrain>($"{observation.TenantId}/{observation.EnvironmentId}").Observe(observation);
    }
}
