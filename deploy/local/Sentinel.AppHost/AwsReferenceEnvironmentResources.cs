using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;

namespace Sentinel.AppHost;

/// <summary>The AWS reference environment's resources (Phase E01): local AWS and the step that publishes the consumer's image.</summary>
internal sealed record AwsReferenceEnvironmentResources(
    IResourceBuilder<ContainerResource> LocalAws,
    IResourceBuilder<ExecutableResource> ConsumerImage,
    LocalAwsPin Pin);
