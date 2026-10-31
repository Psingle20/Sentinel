using Amazon.ECS;
using Amazon.ECS.Model;
using Amazon.SQS;
using Amazon.SQS.Model;
using Shouldly;
using Xunit.Abstractions;
using Task = System.Threading.Tasks.Task;

namespace Reference.Tests.Aws;

/// <summary>
/// Gate check 6 of the compatibility gate: a scenario's reset is a restart of local AWS in memory mode, so a second
/// local AWS holds none of the first one's queues or clusters, and no task container the first one started outlives it.
/// </summary>
[Trait("Category", "Aws")]
[Collection(LocalAwsCollection.Name)]
public sealed class FlociResetTests(ITestOutputHelper output)
{
    [Fact]
    public async Task A_restarted_local_aws_holds_nothing_and_no_task_container_outlives_the_first()
    {
        LocalAws first = new();
        await first.InitializeAsync();
        try
        {
            using AmazonSQSClient sqs = first.Sqs();
            await sqs.CreateQueueAsync(new CreateQueueRequest { QueueName = "orders" });
            using AmazonECSClient ecs = first.Ecs();
            await ecs.CreateClusterAsync(new CreateClusterRequest { ClusterName = "reference" });
            string revision = await EcsWorkload.RegisterAsync(ecs, "1");
            await ecs.CreateServiceAsync(new CreateServiceRequest { Cluster = "reference", ServiceName = EcsWorkload.Service, TaskDefinition = revision, DesiredCount = 1 });
            await EcsWorkload.WaitForAsync(ecs, "reference", service => service.RunningCount == 1);

            await first.StopAsync();
            IReadOnlyList<string> left = await first.StartedContainersAsync();
            output.WriteLine($"After local AWS stopped, {left.Count} container(s) it started still existed.");
        }
        finally
        {
            await first.DisposeAsync();
        }

        (await first.StartedContainersAsync()).ShouldBeEmpty("disposal removes every container local AWS started");

        LocalAws second = new();
        await second.InitializeAsync();
        try
        {
            using AmazonSQSClient sqs = second.Sqs();
            ((await sqs.ListQueuesAsync(new ListQueuesRequest())).QueueUrls ?? []).ShouldBeEmpty();
            using AmazonECSClient ecs = second.Ecs();
            ((await ecs.ListClustersAsync(new ListClustersRequest())).ClusterArns ?? []).ShouldNotContain(arn => arn.EndsWith("/reference", StringComparison.Ordinal));
        }
        finally
        {
            await second.DisposeAsync();
        }
    }
}
