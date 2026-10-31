using System.Text.Json;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Microsoft.Extensions.DependencyInjection;

namespace Sentinel.AppHost;

internal static class AwsReferenceEnvironmentExtensions
{
    /// <summary>The account the scenarios run in; its 12-digit access key is what selects it in the emulator.</summary>
    public const string Account = "000000000000";

    /// <summary>Synthetic: the emulator does not check signatures.</summary>
    public const string SyntheticSecret = "test-not-a-secret";

    /// <summary>
    /// The approved pattern of the rule that routes the producer's order events to the orders queue: Executor restores it
    /// when the route drifts, and holds it as the route's source of truth (Phase E01, Stage 4).
    /// </summary>
    public const string ApprovedOrdersRoute = """{"source":["reference.checkout"],"detail-type":["OrderPlaced"]}""";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Sentinel's side of the AWS environment (Phase E01, Stages 2 and 4): Ingestion reads the consumer's ECS deployments,
    /// its event route's changes, the topology its task definition declares and the orders queue's depth and dead-letter
    /// depth; AgentWorker's investigators read the queue's depth, which exists only here; and Executor quarantines through
    /// the orders queue's redrive policy and restores the route's approved pattern, each with credentials of its own role.
    /// </summary>
    public static void WireSentinel(IResourceBuilder<ContainerResource> emulator, IResourceBuilder<ProjectResource> ingestion, IResourceBuilder<ProjectResource> agentWorker, IResourceBuilder<ProjectResource> executor)
    {
        ingestion
            .WithEnvironment("Ingestion__Providers__Aws__ServiceUrl", emulator.GetEndpoint("http"))
            .WithEnvironment("Ingestion__Providers__Aws__AccessKey", Account)
            .WithEnvironment("Ingestion__Providers__Aws__SecretKey", SyntheticSecret)
            .WithEnvironment("Ingestion__Providers__Aws__Clusters__0", "reference")
            .WaitFor(emulator);
        QueueSeries(ingestion, 0, "queue_depth", "orders:ApproximateNumberOfMessages+ApproximateNumberOfMessagesNotVisible");
        QueueSeries(ingestion, 1, "queue_dead_letter_depth", "orders-dlq:ApproximateNumberOfMessages+ApproximateNumberOfMessagesNotVisible");
        executor
            .WithEnvironment("Executor__Adapters__QueueConsumer__Provider", "sqs")
            .WithEnvironment("Executor__Adapters__Sqs__ServiceUrl", emulator.GetEndpoint("http"))
            .WithEnvironment("Executor__Adapters__Sqs__AccessKey", Account)
            .WithEnvironment("Executor__Adapters__Sqs__SecretKey", SyntheticSecret)
            .WithEnvironment("Executor__Adapters__Sqs__Queues__0", "orders")
            .WithEnvironment("Executor__Adapters__EventBridge__ServiceUrl", emulator.GetEndpoint("http"))
            .WithEnvironment("Executor__Adapters__EventBridge__AccessKey", Account)
            .WithEnvironment("Executor__Adapters__EventBridge__SecretKey", SyntheticSecret)
            .WithEnvironment("Executor__Adapters__EventBridge__Routes__orders-route", ApprovedOrdersRoute)
            .WaitFor(emulator);
        agentWorker.WithEnvironment("Investigation__Metrics__queue_depth", "Messages a consumer's SQS queue holds, waiting or in flight, as SQS reports them.");
    }

    // A series of SQS queue attributes, appended after Ingestion's own series at indexes its configuration leaves unused;
    // the configuration binder reads a list's entries in order whatever their indexes.
    private static void QueueSeries(IResourceBuilder<ProjectResource> ingestion, int index, string name, string expression)
    {
        string prefix = string.Create(System.Globalization.CultureInfo.InvariantCulture, $"Ingestion__MetricSeries__{1000 + index}__");
        ingestion
            .WithEnvironment(prefix + "Name", name)
            .WithEnvironment(prefix + "Expression", expression)
            .WithEnvironment(prefix + "Service", "queue-consumer")
            .WithEnvironment(prefix + "Unit", "messages")
            .WithEnvironment(prefix + "Aggregation", "gauge")
            .WithEnvironment(prefix + "Priority", "8")
            .WithEnvironment(prefix + "Provider", "sqs");
    }

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
