using CleanArchitecture.BuildingBlocks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sentinel.Contracts.Observations;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Runtime.Application;
using Sentinel.Runtime.Application.Incidents;
using Sentinel.Runtime.Application.Abstractions.Runtime;
using Sentinel.Runtime.Application.Detection;

namespace Sentinel.Runtime.Infrastructure.Grains;

/// <summary>
/// Serializes detection and correlation for its partition, so two samples of the same failure can
/// never race to open two incidents. Its state lives in Runtime's correlation tables.
/// </summary>
internal sealed class CorrelationGrain(IServiceScopeFactory scopes) : Grain, ICorrelationGrain
{
    public async Task Observe(Observation observation)
    {
        await using AsyncServiceScope scope = scopes.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<DetectionService>().HandleAsync(observation, CancellationToken.None);
    }
}
