using Amazon.ECS.Model;
using KeyValuePair = Amazon.ECS.Model.KeyValuePair;

namespace Reference.Scenarios.Infrastructure.Environments.Aws;

/// <summary>
/// The queue-consumer's ECS task definition for one revision: the image published under the revision's tag, on the SQS
/// queue, sending its telemetry to the same Prometheus, Loki and Jaeger as the local workloads. A revision differs only
/// in the revision it serves, as the local consumer's revisions do. The task names its queues and the services it calls,
/// as a real deployment does, which is where Sentinel reads the consumer's topology from.
/// </summary>
public static class ConsumerTask
{
    public const string Container = "consumer";

    public static RegisterTaskDefinitionRequest Definition(AwsEnvironmentOptions options, string revision)
    {
        ArgumentNullException.ThrowIfNull(options);

        return new RegisterTaskDefinitionRequest
        {
            Family = options.Service,
            ContainerDefinitions =
            [
                new ContainerDefinition
                {
                    Name = Container,
                    Image = options.Image.Replace("{revision}", revision, StringComparison.Ordinal),
                    Essential = true,
                    Memory = 256,
                    Environment =
                    [
                        .. options.TaskEnvironment.Select(variable => new KeyValuePair { Name = variable.Key, Value = variable.Value }),
                        new KeyValuePair { Name = "Reference__Queue__Sqs__QueueName", Value = options.Queue },
                        new KeyValuePair { Name = "Reference__Queue__Sqs__DeadLetterQueueName", Value = options.DeadLetterQueue },
                        new KeyValuePair { Name = "Reference__InitialRevision", Value = revision }
                    ]
                }
            ]
        };
    }
}
