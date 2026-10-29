using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Configuration;
using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.Ingestion.Application.ControlPlane;

namespace Sentinel.Ingestion.Application.BusinessTasks.Queries.Configuration;

/// <summary>The current document, seeded from the deployment configuration until a version is published.</summary>
public sealed class GetSourceConfigurationQueryHandler(VersionedDocumentPublisher<SourceConfigurationDocument> publisher, IngestionDeploymentOptions deployment) : IQueryHandler<GetSourceConfigurationQuery, DocumentState<SourceConfigurationDocument>>
{
    public async Task<Result<DocumentState<SourceConfigurationDocument>>> Handle(GetSourceConfigurationQuery query, CancellationToken cancellationToken) =>
        Result.Success(await publisher.CurrentAsync(() => SourceConfigurationMapping.From(deployment.Value), cancellationToken));
}
