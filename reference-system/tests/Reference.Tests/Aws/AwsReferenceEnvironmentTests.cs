using Amazon.ECS;
using Amazon.ECS.Model;
using Amazon.SQS;
using Amazon.SQS.Model;
using Microsoft.Extensions.Logging.Abstractions;
using Reference.Scenarios.Application.Abstractions;
using Reference.Scenarios.Domain.Scenarios;
using Reference.Scenarios.Infrastructure.Environments.Aws;
using Reference.Scenarios.Infrastructure.Environments.Local;
using Shouldly;
using Task = System.Threading.Tasks.Task;

namespace Reference.Tests.Aws;

/// <summary>
/// The AWS environment's lifecycle against the pinned emulator (Phase E01, Stage 1): provisioning creates the queues and
/// the consumer's ECS service once, steps for the consumer go through ECS and SQS, steps for anything else go to the local
/// environment, and a reset proves that nothing of a run remains and the baseline revision serves again. A stand-in image
/// already local plays the consumer, so these tests check the environment, not the workload.
/// </summary>
[Trait("Category", "Emulator")]
[Collection(EmulatorCollection.Name)]
public sealed class AwsReferenceEnvironmentTests(FlociEmulator emulator) : IClassFixture<FlociEmulator>
{
    [Fact]
    public async Task Provisioning_steps_and_a_repeated_reset_leave_the_baseline_and_empty_queues()
    {
        string suffix = Guid.NewGuid().ToString("N")[..8];
        var options = new AwsEnvironmentOptions
        {
            ServiceUrl = emulator.Endpoint,
            Cluster = $"reference-{suffix}",
            Queue = $"orders-{suffix}",
            DeadLetterQueue = $"orders-{suffix}-dlq",
            Image = "redis:7-alpine",
            Emulator = $"{FlociEmulator.Pin.Emulator} {FlociEmulator.Pin.Version}",
            ImageDigest = FlociEmulator.Pin.Digest
        };
        using AmazonSQSClient sqs = emulator.Sqs();
        using AmazonECSClient ecs = emulator.Ecs();
        using var provisioning = new AwsProvisioning();
        var control = new RecordingControl();
        var environment = new AwsReferenceEnvironment(sqs, ecs, options, provisioning, new LocalReferenceEnvironment(control), TimeProvider.System, NullLogger<AwsReferenceEnvironment>.Instance);

        (await environment.InspectAsync(CancellationToken.None)).Ready.ShouldBeFalse("nothing is provisioned yet");

        await environment.ResetAsync(CancellationToken.None);
        await environment.ResetAsync(CancellationToken.None);
        EnvironmentState state = await environment.InspectAsync(CancellationToken.None);
        state.Ready.ShouldBeTrue(string.Join("; ", state.Problems));
        state.Emulator.ShouldBe($"{FlociEmulator.Pin.Emulator} {FlociEmulator.Pin.Version}");
        state.ProvisioningVersion.ShouldBe(AwsReferenceEnvironment.ProvisioningVersion);
        control.Resets.ShouldBe(2, "every reset resets the local workloads too");
        provisioning.TaskDefinitions.Keys.Order(StringComparer.Ordinal).ToArray().ShouldBe(["v1.4.0", "v1.4.1"], customMessage: "provisioning ran once");
        (await ServiceAsync(ecs, options)).TaskDefinition.ShouldBe(provisioning.TaskDefinitions["v1.4.0"]);

        await environment.ActivateAsync(Step("deploy", "queue-consumer", "v1.4.1"), "run-1", CancellationToken.None);
        await environment.ActivateAsync(Step("poisonMessage", "queue-consumer"), "run-1", CancellationToken.None);
        await environment.ActivateAsync(Step("deploy", "order-service", "v1.7.1"), "run-1", CancellationToken.None);
        (await ServiceAsync(ecs, options)).TaskDefinition.ShouldBe(provisioning.TaskDefinitions["v1.4.1"]);
        (await DepthAsync(sqs, options.Queue)).ShouldBe(1, "the poison message waits in the queue");
        control.Deployments.ShouldBe(["order-service v1.7.1"], "a step for a local workload goes to the local environment");

        ReferenceEnvironmentException unsupported = await Should.ThrowAsync<ReferenceEnvironmentException>(
            () => environment.ActivateAsync(Step("duplicatePublication", "queue-consumer"), "run-1", CancellationToken.None));
        unsupported.Failure.ShouldBe(EnvironmentFailure.Unsupported);

        await environment.ResetAsync(CancellationToken.None);
        (await DepthAsync(sqs, options.Queue)).ShouldBe(0, "no message of the run survives a reset");
        (await ServiceAsync(ecs, options)).TaskDefinition.ShouldBe(provisioning.TaskDefinitions["v1.4.0"], "the baseline revision serves again");
        (await ServiceAsync(ecs, options)).RunningCount.ShouldBe(1);
    }

    private static ScenarioStep Step(string action, string service, string? version = null) =>
        new() { AtSeconds = 0, Action = action, Service = service, Version = version, Role = "noise" };

    private static async Task<Service> ServiceAsync(AmazonECSClient ecs, AwsEnvironmentOptions options) =>
        (await ecs.DescribeServicesAsync(new DescribeServicesRequest { Cluster = options.Cluster, Services = [options.Service] })).Services!.First(service => service.Status == "ACTIVE");

    private static async Task<int> DepthAsync(AmazonSQSClient sqs, string queue)
    {
        string url = (await sqs.GetQueueUrlAsync(queue)).QueueUrl;
        Dictionary<string, string> attributes = (await sqs.GetQueueAttributesAsync(new GetQueueAttributesRequest { QueueUrl = url, AttributeNames = ["ApproximateNumberOfMessages", "ApproximateNumberOfMessagesNotVisible"] })).Attributes ?? [];
        return attributes.Values.Sum(int.Parse);
    }

    private sealed class RecordingControl : IReferenceControl
    {
        public int Resets { get; private set; }

        public List<string> Deployments { get; } = [];

        public Task ResetAsync(CancellationToken cancellationToken)
        {
            Resets++;
            return Task.CompletedTask;
        }

        public Task DeployAsync(ScenarioStep step, string runId, CancellationToken cancellationToken)
        {
            Deployments.Add($"{step.Service} {step.Version}");
            return Task.CompletedTask;
        }

        public Task StartBatchAsync(ScenarioStep step, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StopBatchAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task InjectPoisonMessageAsync(ScenarioStep step, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task DuplicatePublicationAsync(ScenarioStep step, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task SlowConfirmationsAsync(ScenarioStep step, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
