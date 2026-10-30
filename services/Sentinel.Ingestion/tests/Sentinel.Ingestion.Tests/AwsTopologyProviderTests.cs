using Amazon.ECS;
using Amazon.ECS.Model;
using Amazon.SQS;
using Amazon.SQS.Model;
using Microsoft.Extensions.Logging.Abstractions;
using Sentinel.Contracts;
using Sentinel.Contracts.Observations;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Contracts.Queries;
using Sentinel.Ingestion.Application.Abstractions.Providers;
using Sentinel.Ingestion.Infrastructure.Providers;
using Shouldly;
using KeyValuePair = Amazon.ECS.Model.KeyValuePair;
using Task = System.Threading.Tasks.Task;

namespace Sentinel.Ingestion.Tests;

/// <summary>
/// The AWS topology provider against the pinned emulator (Phase E01, Stage 2): an ECS service depends on the service its
/// task definition is told to call through service discovery and on each queue of the account it is given by name or by
/// URL, and on nothing else its environment holds; an edge records which variable declared it, never the variable's value.
/// An emulator that does not answer is an unavailable result.
/// </summary>
[Collection(AwsEmulatorCollection.Name)]
[Trait("Category", "Emulator")]
public sealed class AwsTopologyProviderTests(AwsEmulator emulator)
{
    private static readonly TenantId Tenant = TenantId.From("acme");
    private static readonly EnvironmentId Environment = EnvironmentId.From("reference");

    [Fact]
    public async Task An_ecs_service_depends_on_what_its_task_definition_names_and_on_nothing_else()
    {
        using AmazonECSClient ecs = emulator.Ecs();
        using AmazonSQSClient sqs = emulator.Sqs();
        string suffix = Guid.NewGuid().ToString("N")[..8];
        string queue = $"orders-{suffix}";
        await sqs.CreateQueueAsync(new CreateQueueRequest { QueueName = queue });
        string deadLetters = (await sqs.CreateQueueAsync(new CreateQueueRequest { QueueName = $"{queue}-dlq" })).QueueUrl;
        string cluster = $"topology-{suffix}";
        await ecs.CreateClusterAsync(new CreateClusterRequest { ClusterName = cluster });
        string definition = (await ecs.RegisterTaskDefinitionAsync(new RegisterTaskDefinitionRequest
        {
            Family = "queue-consumer",
            ContainerDefinitions =
            [
                new ContainerDefinition
                {
                    Name = "consumer",
                    Image = "sentinel-ingestion-topology-test:v1.4.0",
                    Essential = true,
                    Memory = 64,
                    Environment =
                    [
                        new KeyValuePair { Name = "services__order-service__http__0", Value = "http://host.docker.internal:5000" },
                        new KeyValuePair { Name = "Reference__Queue__Sqs__QueueName", Value = queue },
                        new KeyValuePair { Name = "Reference__Queue__Sqs__DeadLetterQueueUrl", Value = deadLetters },
                        new KeyValuePair { Name = "Reference__Queue__Sqs__ServiceUrl", Value = "http://host.docker.internal:4566/" },
                        new KeyValuePair { Name = "Reference__Queue__Sqs__AccessKey", Value = AwsEmulator.Account }
                    ]
                }
            ]
        })).TaskDefinition.TaskDefinitionArn;
        await ecs.CreateServiceAsync(new CreateServiceRequest { Cluster = cluster, ServiceName = "queue-consumer", TaskDefinition = definition, DesiredCount = 0 });

        ProviderResult<TopologyObservation> result = await Provider(ecs, sqs, cluster).GetDependenciesAsync(Request(), CancellationToken.None);

        result.Status.ShouldBe(ProviderStatus.Healthy, result.Error);
        result.Observations.Select(edge => (edge.ServiceId.Value, edge.TargetServiceId.Value, edge.DependencyType, edge.Protocol)).ShouldBe(
            [("queue-consumer", "order-service", "calls", "http"), ("queue-consumer", queue, "queue", "sqs"), ("queue-consumer", $"{queue}-dlq", "queue", "sqs")],
            ignoreOrder: true);
        result.Observations.Select(edge => edge.Attributes["declaredBy"]).ShouldBe(
            ["services__order-service__http__0", "Reference__Queue__Sqs__QueueName", "Reference__Queue__Sqs__DeadLetterQueueUrl"],
            ignoreOrder: true);
        result.Observations.ShouldAllBe(edge => edge.SourceProvider == AwsTopologyProvider.ProviderName && edge.Confidence < 1 && edge.FirstSeen <= edge.LastSeen);
        result.Observations.Select(edge => edge.ObservationId).ShouldBeUnique();
    }

    [Fact]
    public async Task An_emulator_that_does_not_answer_is_an_unavailable_result()
    {
        var credentials = new Amazon.Runtime.BasicAWSCredentials(AwsEmulator.Account, AwsEmulator.Secret);
        using var ecs = new AmazonECSClient(credentials, new AmazonECSConfig { ServiceURL = "http://127.0.0.1:9", AuthenticationRegion = AwsEmulator.Region, MaxErrorRetry = 0, Timeout = TimeSpan.FromSeconds(2) });
        using var sqs = new AmazonSQSClient(credentials, new AmazonSQSConfig { ServiceURL = "http://127.0.0.1:9", AuthenticationRegion = AwsEmulator.Region, MaxErrorRetry = 0, Timeout = TimeSpan.FromSeconds(2) });

        ProviderResult<TopologyObservation> result = await Provider(ecs, sqs, "reference").GetDependenciesAsync(Request(), CancellationToken.None);

        result.Status.ShouldBe(ProviderStatus.Unavailable);
        result.Observations.ShouldBeEmpty();
    }

    private AwsTopologyProvider Provider(IAmazonECS ecs, IAmazonSQS sqs, string cluster) => new(
        ecs,
        sqs,
        new AwsProviderOptions { ServiceUrl = emulator.Endpoint, AccessKey = AwsEmulator.Account, SecretKey = AwsEmulator.Secret, Clusters = [cluster] },
        TimeProvider.System,
        NullLogger<AwsTopologyProvider>.Instance);

    private static TopologyRequest Request() => new(Tenant, Environment, new TimeRange(DateTimeOffset.UtcNow.AddMinutes(-15), DateTimeOffset.UtcNow));
}
