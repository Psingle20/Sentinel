using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Configuration;
using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.Runtime.Application.ControlPlane;

namespace Sentinel.Runtime.Application.BusinessTasks.Queries.Configuration;

/// <summary>The current document, seeded from the deployment configuration until a version is published.</summary>
public sealed class GetRuntimeConfigurationQueryHandler(VersionedDocumentPublisher<RuntimeConfigurationDocument> publisher, RuntimeDeploymentOptions deployment) : IQueryHandler<GetRuntimeConfigurationQuery, DocumentState<RuntimeConfigurationDocument>>
{
    public async Task<Result<DocumentState<RuntimeConfigurationDocument>>> Handle(GetRuntimeConfigurationQuery query, CancellationToken cancellationToken) =>
        Result.Success(await publisher.CurrentAsync(() => RuntimeConfigurationMapping.From(deployment.Value), cancellationToken));
}
