using Sentinel.Contracts;
using Sentinel.Contracts.Observations;
using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.Ingestion.Application.Abstractions.Providers;

public interface ITopologyProvider
{
    string Name { get; }

    Task<ProviderResult<TopologyObservation>> GetDependenciesAsync(TopologyRequest request, CancellationToken cancellationToken);
}
