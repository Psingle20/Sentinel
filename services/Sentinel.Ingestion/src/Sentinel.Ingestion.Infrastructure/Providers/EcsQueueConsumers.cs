using Amazon.ECS;
using Amazon.ECS.Model;
using KeyValuePair = Amazon.ECS.Model.KeyValuePair;

namespace Sentinel.Ingestion.Infrastructure.Providers;

/// <summary>
/// Which ECS service consumes each queue: the services of the configured clusters whose task definition names the queue in
/// its environment, by name or by its URL, as the AWS topology provider reads it.
/// </summary>
internal static class EcsQueueConsumers
{
    public static async Task<Dictionary<string, string>> FindAsync(IAmazonECS ecs, IReadOnlyList<string> clusters, IReadOnlyCollection<string> queues, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ecs);
        ArgumentNullException.ThrowIfNull(clusters);
        ArgumentNullException.ThrowIfNull(queues);

        var consumers = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string cluster in clusters)
        {
            foreach (Service service in (await EcsServices.ListAsync(ecs, cluster, cancellationToken)).Where(service => service.Status == "ACTIVE"))
            {
                TaskDefinition definition = (await ecs.DescribeTaskDefinitionAsync(new DescribeTaskDefinitionRequest { TaskDefinition = service.TaskDefinition }, cancellationToken)).TaskDefinition;
                foreach (KeyValuePair variable in (definition.ContainerDefinitions ?? []).SelectMany(container => container.Environment ?? []))
                {
                    string value = (variable.Value ?? string.Empty).TrimEnd('/');
                    string named = value[(value.LastIndexOf('/') + 1)..];
                    if (queues.Contains(named))
                    {
                        consumers.TryAdd(named, service.ServiceName);
                    }
                }
            }
        }

        return consumers;
    }
}
