using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Configuration;
using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.AgentWorker.Application.ControlPlane;

namespace Sentinel.AgentWorker.Application.BusinessTasks.Queries.Configuration;

/// <summary>The last hundred published versions, newest first, with the deployment configuration last.</summary>
public sealed class GetModelConfigurationHistoryQueryHandler(VersionedDocumentPublisher<ModelConfigurationDocument> publisher, ModelDeploymentOptions deployment) : IQueryHandler<GetModelConfigurationHistoryQuery, IReadOnlyList<DocumentVersion>>
{
    public async Task<Result<IReadOnlyList<DocumentVersion>>> Handle(GetModelConfigurationHistoryQuery query, CancellationToken cancellationToken) =>
        Result.Success(await publisher.HistoryAsync(deployment.Seed, 100, cancellationToken));
}
