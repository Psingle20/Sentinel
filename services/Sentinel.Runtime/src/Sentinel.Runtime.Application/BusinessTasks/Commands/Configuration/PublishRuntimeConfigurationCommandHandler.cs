using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Configuration;
using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.Runtime.Application.Abstractions.Configuration;
using Sentinel.Runtime.Application.ControlPlane;
using Sentinel.Runtime.Application.Options;

namespace Sentinel.Runtime.Application.BusinessTasks.Commands.Configuration;

/// <summary>
/// Publishes a new version over the one it replaces, once it satisfies the document's own rules and
/// Runtime's startup rules on the options it would produce; it applies here at once. Workflows
/// already running keep their timers.
/// </summary>
public sealed class PublishRuntimeConfigurationCommandHandler(
    VersionedDocumentPublisher<RuntimeConfigurationDocument> publisher,
    RuntimeDeploymentOptions deployment,
    IRuntimeConfigurationActivator activator) : ICommandHandler<PublishRuntimeConfigurationCommand, DocumentVersion>
{
    public async Task<Result<DocumentVersion>> Handle(PublishRuntimeConfigurationCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        Result<PublishedDocument<RuntimeConfigurationDocument>> published = await publisher.PublishAsync(
            command.Document, command.ExpectedVersion, command.Author, () => RuntimeConfigurationMapping.From(deployment.Value), Validate, cancellationToken);
        if (published.IsFailure)
        {
            return Result.Failure<DocumentVersion>(published.Error);
        }

        activator.Activate(published.Value);
        return Result.Success(published.Value.Version);
    }

    private IReadOnlyList<string> Validate(RuntimeConfigurationDocument document)
    {
        IReadOnlyList<string> problems = RuntimeConfigurationMapping.Problems(document);
        if (problems.Count > 0)
        {
            return problems;
        }

        RuntimeOptions candidate = deployment.Create();
        RuntimeConfigurationMapping.Apply(candidate, document);
        return candidate.IsValid() ? [] : ["The configuration does not satisfy Runtime's startup rules."];
    }
}
