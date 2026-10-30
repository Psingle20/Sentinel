using System.Diagnostics;
using Amazon.ECS;
using Amazon.ECS.Model;
using KeyValuePair = Amazon.ECS.Model.KeyValuePair;

namespace Reference.Tests.Aws;

/// <summary>A stand-in for the queue consumer as an ECS service: a small real container from an image already local.</summary>
public static class EcsWorkload
{
    public const string Service = "queue-consumer";

    // An image local orchestration already pulls, so that an ECS task never downloads anything.
    private const string Image = "redis:7-alpine";

    public static async Task<string> RegisterAsync(AmazonECSClient ecs, string revision) =>
        (await ecs.RegisterTaskDefinitionAsync(new RegisterTaskDefinitionRequest
        {
            Family = Service,
            ContainerDefinitions =
            [
                new ContainerDefinition
                {
                    Name = "consumer",
                    Image = Image,
                    Essential = true,
                    Memory = 64,
                    Command = ["redis-server", "--save", "", "--appendonly", "no"],
                    Environment = [new KeyValuePair { Name = "CONSUMER_REVISION", Value = revision }]
                }
            ]
        })).TaskDefinition.TaskDefinitionArn;

    public static async Task<Service> WaitForAsync(AmazonECSClient ecs, string cluster, Func<Service, bool> condition)
    {
        var deadline = Stopwatch.StartNew();
        while (true)
        {
            Service service = (await ecs.DescribeServicesAsync(new DescribeServicesRequest { Cluster = cluster, Services = [Service] })).Services.Single();
            if (condition(service))
            {
                return service;
            }

            if (deadline.Elapsed > TimeSpan.FromMinutes(2))
            {
                throw new TimeoutException($"{Service}: running {service.RunningCount}, desired {service.DesiredCount}, revision {service.TaskDefinition}, rollout {service.Deployments?.FirstOrDefault()?.RolloutState}.");
            }

            await System.Threading.Tasks.Task.Delay(TimeSpan.FromSeconds(2));
        }
    }
}
