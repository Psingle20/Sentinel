using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;

namespace Sentinel.AppHost;

/// <summary>The AWS-emulated reference environment's resources (Phase E01): the emulator and the step that publishes the consumer's image.</summary>
internal sealed record AwsReferenceEnvironmentResources(
    IResourceBuilder<ContainerResource> Emulator,
    IResourceBuilder<ExecutableResource> ConsumerImage,
    AwsEmulatorPin Pin);
