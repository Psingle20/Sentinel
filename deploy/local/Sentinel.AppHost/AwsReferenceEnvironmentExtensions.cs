using System.Text.Json;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Microsoft.Extensions.DependencyInjection;

namespace Sentinel.AppHost;

internal static class AwsReferenceEnvironmentExtensions
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Adds the AWS-emulated reference environment (Phase E01, ADR-021), enabled by
    /// <c>Sentinel:ReferenceEnvironment=aws</c>: the pinned emulator, with the Docker socket mounted so that ECS tasks run
    /// as real containers on a network of their own, capped in memory and CPU; the step that publishes the
    /// queue-consumer's image into the local Docker daemon, where the emulator finds it without pulling; and the scenario
    /// runner switched to the AWS environment, which provisions the queues and the ECS service itself. The access key is
    /// synthetic and only selects the account; nothing reads a developer's AWS configuration.
    /// </summary>
    public static AwsReferenceEnvironmentResources AddAwsReferenceEnvironment(this IDistributedApplicationBuilder builder, string root, IResourceBuilder<ProjectResource> scenarioRunner)
    {
        AwsEmulatorPin pin = JsonSerializer.Deserialize<AwsEmulatorPin>(File.ReadAllText(Path.Combine(root, "deploy", "aws", "emulator.json")), Json)!;
        builder.Services.AddHostedService<AwsEnvironmentLifecycle>();

        IResourceBuilder<ContainerResource> emulator = builder.AddContainer("aws-emulator", pin.Image, pin.Version)
            .WithImageSHA256(pin.Digest["sha256:".Length..])
            .WithHttpEndpoint(targetPort: pin.Port, name: "http")
            .WithBindMount("/var/run/docker.sock", "/var/run/docker.sock")
            .WithContainerRuntimeArgs("--user", "root", "--memory", "768m", "--cpus", "1.5")
            .WithEnvironment("FLOCI_HOSTNAME", "aws-emulator")
            .WithEnvironment("FLOCI_SERVICES_ECS_DOCKER_NETWORK", AwsEnvironmentLifecycle.Network)
            .WithEnvironment("FLOCI_SERVICES_ECS_IMAGE_PULL_BEHAVIOR", "prefer-cached")
            .WithEnvironment("FLOCI_DOCKER_EXTRA_LABELS_0__KEY", AwsEnvironmentLifecycle.LabelKey)
            .WithEnvironment("FLOCI_DOCKER_EXTRA_LABELS_0__VALUE", AwsEnvironmentLifecycle.LabelValue)
            .WithHttpHealthCheck("/_localstack/health");

        IResourceBuilder<ExecutableResource> image = builder.AddExecutable(
            "queue-consumer-image",
            "dotnet",
            Path.Combine(root, "services", "Reference.QueueConsumer"),
            "publish", "-t:PublishContainer", "-c", "Release", "--nologo", "-v", "quiet");

        scenarioRunner
            .WithEnvironment("Reference__Environment", "aws")
            .WithEnvironment("Reference__Aws__ServiceUrl", emulator.GetEndpoint("http"))
            .WithEnvironment("Reference__Aws__Emulator", $"{pin.Emulator} {pin.Version}")
            .WithEnvironment("Reference__Aws__ImageDigest", pin.Digest)
            .WaitFor(emulator)
            .WaitForCompletion(image);

        return new AwsReferenceEnvironmentResources(emulator, image, pin);
    }
}
