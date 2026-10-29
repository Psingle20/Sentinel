using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Configuration;
using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.AgentWorker.Application.ControlPlane;

namespace Sentinel.AgentWorker.Application.BusinessTasks.Queries.Configuration;

/// <summary>The current document, seeded from the deployment configuration until a version is published.</summary>
public sealed class GetModelConfigurationQueryHandler(VersionedDocumentPublisher<ModelConfigurationDocument> publisher, ModelDeploymentOptions deployment) : IQueryHandler<GetModelConfigurationQuery, DocumentState<ModelConfigurationDocument>>
{
    public async Task<Result<DocumentState<ModelConfigurationDocument>>> Handle(GetModelConfigurationQuery query, CancellationToken cancellationToken) =>
        Result.Success(await publisher.CurrentAsync(deployment.Seed, cancellationToken));
}
