using Amazon.ECS.Model;
using KeyValuePair = Amazon.ECS.Model.KeyValuePair;

namespace Reference.Scenarios.Infrastructure.Environments.Aws;

/// <summary>
/// The queue-consumer's ECS task definition for one revision: the published image, on the SQS queue, sending its
/// telemetry to the same Prometheus, Loki and Jaeger as the local workloads. A revision differs only in the revision it
/// serves, as the local consumer's revisions do.
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
                    Image = options.Image,
                    Essential = true,
                    Memory = 256,
                    Environment =
                    [
                        .. options.TaskEnvironment.Select(variable => new KeyValuePair { Name = variable.Key, Value = variable.Value }),
                        new KeyValuePair { Name = "Reference__InitialRevision", Value = revision }
                    ]
                }
            ]
        };
    }
}
