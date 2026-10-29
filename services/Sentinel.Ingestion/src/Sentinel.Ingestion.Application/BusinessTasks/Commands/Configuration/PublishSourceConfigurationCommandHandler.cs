using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Configuration;
using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.Ingestion.Application.Abstractions.Configuration;
using Sentinel.Ingestion.Application.ControlPlane;
using Sentinel.Ingestion.Application.Options;

namespace Sentinel.Ingestion.Application.BusinessTasks.Commands.Configuration;

/// <summary>
/// Publishes a new version over the one it replaces, once it satisfies the document's own rules and
/// Ingestion's startup rules on the options it would produce; it applies here at once.
/// </summary>
public sealed class PublishSourceConfigurationCommandHandler(
    VersionedDocumentPublisher<SourceConfigurationDocument> publisher,
    IngestionDeploymentOptions deployment,
    ISourceConfigurationActivator activator) : ICommandHandler<PublishSourceConfigurationCommand, DocumentVersion>
{
    public async Task<Result<DocumentVersion>> Handle(PublishSourceConfigurationCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        Result<PublishedDocument<SourceConfigurationDocument>> published = await publisher.PublishAsync(
            command.Document, command.ExpectedVersion, command.Author, () => SourceConfigurationMapping.From(deployment.Value), Validate, cancellationToken);
        if (published.IsFailure)
        {
            return Result.Failure<DocumentVersion>(published.Error);
        }

        activator.Activate(published.Value);
        return Result.Success(published.Value.Version);
    }

    private IReadOnlyList<string> Validate(SourceConfigurationDocument document)
    {
        IReadOnlyList<string> problems = SourceConfigurationMapping.Problems(document);
        if (problems.Count > 0)
        {
            return problems;
        }

        IngestionOptions candidate = deployment.Create();
        SourceConfigurationMapping.Apply(candidate, document);
        return candidate.IsValid() ? [] : ["The configuration does not satisfy Ingestion's startup rules."];
    }
}
