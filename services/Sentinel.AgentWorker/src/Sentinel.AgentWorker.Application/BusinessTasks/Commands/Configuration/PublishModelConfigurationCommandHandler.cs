using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Configuration;
using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.AgentWorker.Application.Abstractions.Configuration;
using Sentinel.AgentWorker.Application.Abstractions.Providers;
using Sentinel.AgentWorker.Application.ControlPlane;

namespace Sentinel.AgentWorker.Application.BusinessTasks.Commands.Configuration;

/// <summary>
/// Publishes a new version over the one it replaces, once it satisfies the document's rules, including
/// that every provider it routes to is configured in this deployment; it applies here at once.
/// </summary>
public sealed class PublishModelConfigurationCommandHandler(
    VersionedDocumentPublisher<ModelConfigurationDocument> publisher,
    ModelDeploymentOptions deployment,
    IModelRouter models,
    IModelConfigurationActivator activator) : ICommandHandler<PublishModelConfigurationCommand, DocumentVersion>
{
    public async Task<Result<DocumentVersion>> Handle(PublishModelConfigurationCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        IReadOnlyCollection<string> configured = models.Configured;
        Result<PublishedDocument<ModelConfigurationDocument>> published = await publisher.PublishAsync(
            command.Document, command.ExpectedVersion, command.Author, deployment.Seed, candidate => ModelConfigurationMapping.Problems(candidate, configured), cancellationToken);
        if (published.IsFailure)
        {
            return Result.Failure<DocumentVersion>(published.Error);
        }

        activator.Activate(published.Value);
        return Result.Success(published.Value.Version);
    }
}
