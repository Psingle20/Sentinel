using Sentinel.Contracts;
using Sentinel.Contracts.Observations;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Contracts.Queries;

namespace Sentinel.Ingestion.Application.Abstractions.Providers;

public interface IDeploymentProvider
{
    string Name { get; }

    Task<ProviderResult<DeploymentObservation>> GetDeploymentsAsync(DeploymentRequest request, CancellationToken cancellationToken);
}
