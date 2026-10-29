using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Configuration;
using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.Ingestion.Application.ControlPlane;

namespace Sentinel.Ingestion.Application.BusinessTasks.Queries.Configuration;

/// <summary>The last hundred published versions, newest first, with the deployment configuration last.</summary>
public sealed class GetSourceConfigurationHistoryQueryHandler(VersionedDocumentPublisher<SourceConfigurationDocument> publisher, IngestionDeploymentOptions deployment) : IQueryHandler<GetSourceConfigurationHistoryQuery, IReadOnlyList<DocumentVersion>>
{
    public async Task<Result<IReadOnlyList<DocumentVersion>>> Handle(GetSourceConfigurationHistoryQuery query, CancellationToken cancellationToken) =>
        Result.Success(await publisher.HistoryAsync(() => SourceConfigurationMapping.From(deployment.Value), 100, cancellationToken));
}
