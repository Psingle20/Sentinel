using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Configuration;
using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.Runtime.Application.ControlPlane;

namespace Sentinel.Runtime.Application.BusinessTasks.Queries.Configuration;

/// <summary>The last hundred published versions, with the deployment configuration as the first.</summary>
public sealed class GetRuntimeConfigurationHistoryQueryHandler(VersionedDocumentPublisher<RuntimeConfigurationDocument> publisher, RuntimeDeploymentOptions deployment) : IQueryHandler<GetRuntimeConfigurationHistoryQuery, IReadOnlyList<DocumentVersion>>
{
    public async Task<Result<IReadOnlyList<DocumentVersion>>> Handle(GetRuntimeConfigurationHistoryQuery query, CancellationToken cancellationToken) =>
        Result.Success(await publisher.HistoryAsync(() => RuntimeConfigurationMapping.From(deployment.Value), 100, cancellationToken));
}
