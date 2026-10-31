using Amazon.ECS;
using Amazon.ECS.Model;
using Amazon.EventBridge;
using Amazon.EventBridge.Model;
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
/// The EventBridge route provider against local AWS (Phase E01, Stage 4): the first sight of a rule is its
/// baseline, a changed pattern is a change of kind route to the consumer whose ECS task definition names the rule's
/// target queue, with both patterns as attributes, and a later pull reports the same change without a new one.
/// </summary>
[Collection(LocalAwsCollection.Name)]
[Trait("Category", "Aws")]
public sealed class EventBridgeRouteProviderTests(LocalAws localAws)
{
    private const string Approved = """{"source":["reference.checkout"],"detail-type":["OrderPlaced"]}""";
    private const string Drifted = """{"source":["reference.storefront"],"detail-type":["OrderPlaced"]}""";

    private static readonly TenantId Tenant = TenantId.From("acme");
    private static readonly EnvironmentId Environment = EnvironmentId.From("reference");

    [Fact]
    public async Task A_changed_pattern_is_a_route_change_of_the_consumer_whose_task_names_the_target_queue()
    {
        using AmazonECSClient ecs = localAws.Ecs();
        using AmazonSQSClient sqs = localAws.Sqs();
        using AmazonEventBridgeClient events = localAws.Events();
        string suffix = Guid.NewGuid().ToString("N")[..8];
        string queue = $"orders-{suffix}";
        string queueUrl = (await sqs.CreateQueueAsync(new CreateQueueRequest { QueueName = queue })).QueueUrl;
        string queueArn = (await sqs.GetQueueAttributesAsync(new GetQueueAttributesRequest { QueueUrl = queueUrl, AttributeNames = ["QueueArn"] })).Attributes["QueueArn"];
        string cluster = $"routes-{suffix}";
        await ecs.CreateClusterAsync(new CreateClusterRequest { ClusterName = cluster });
        string definition = (await ecs.RegisterTaskDefinitionAsync(new RegisterTaskDefinitionRequest
        {
            Family = "queue-consumer",
            ContainerDefinitions = [new ContainerDefinition { Name = "consumer", Image = "sentinel-ingestion-route-test:v1.4.0", Essential = true, Memory = 64, Environment = [new KeyValuePair { Name = "Reference__Queue__Sqs__QueueName", Value = queue }] }]
        })).TaskDefinition.TaskDefinitionArn;
        await ecs.CreateServiceAsync(new CreateServiceRequest { Cluster = cluster, ServiceName = "queue-consumer", TaskDefinition = definition, DesiredCount = 0 });
        string rule = $"orders-route-{suffix}";
        await events.PutRuleAsync(new PutRuleRequest { Name = rule, EventPattern = Approved, State = RuleState.ENABLED });
        await events.PutTargetsAsync(new PutTargetsRequest { Rule = rule, Targets = [new Target { Id = queue, Arn = queueArn }] });
        var provider = new EventBridgeRouteProvider(events, ecs, new AwsProviderOptions { ServiceUrl = localAws.Endpoint, AccessKey = LocalAws.Account, SecretKey = LocalAws.Secret, Clusters = [cluster] }, TimeProvider.System, NullLogger<EventBridgeRouteProvider>.Instance);
        DateTimeOffset from = DateTimeOffset.UtcNow.AddMinutes(-1);

        ProviderResult<DeploymentObservation> baseline = await provider.GetDeploymentsAsync(Request(from), CancellationToken.None);
        baseline.Status.ShouldBe(ProviderStatus.Healthy, baseline.Error);
        baseline.Observations.ShouldNotContain(observation => observation.Attributes["rule"] == rule, "a rule's first sight is its baseline");

        await events.PutRuleAsync(new PutRuleRequest { Name = rule, EventPattern = Drifted, State = RuleState.ENABLED });
        ProviderResult<DeploymentObservation> changed = await provider.GetDeploymentsAsync(Request(from), CancellationToken.None);

        DeploymentObservation[] route = [.. changed.Observations.Where(observation => observation.Attributes["rule"] == rule)];
        route.Select(observation => observation.Status).ShouldBe(["in-progress", "completed"]);
        DeploymentObservation completed = route[^1];
        completed.ChangeKind.ShouldBe("route");
        completed.ServiceId.ShouldBe(ServiceId.From("queue-consumer"), "the change is the consumer's, whose task names the rule's queue");
        completed.Attributes["queue"].ShouldBe(queue);
        completed.Attributes["pattern"].ShouldBe(Drifted);
        completed.Attributes["previousPattern"].ShouldBe(Approved);
        completed.Version.ShouldNotBe(completed.PreviousVersion);
        completed.CompletedAt.ShouldBe(completed.StartedAt, "a route change takes effect at once");

        ProviderResult<DeploymentObservation> again = await provider.GetDeploymentsAsync(Request(from), CancellationToken.None);
        again.Observations.Where(observation => observation.Attributes["rule"] == rule).Select(observation => observation.ObservationId).ShouldBe(route.Select(observation => observation.ObservationId), "an unchanged rule adds no change");
    }

    private static DeploymentRequest Request(DateTimeOffset from) => new(Tenant, Environment, new TimeRange(from, DateTimeOffset.UtcNow.AddMinutes(5)));
}
