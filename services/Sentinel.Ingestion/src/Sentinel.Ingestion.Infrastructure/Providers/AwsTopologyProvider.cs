using System.Globalization;
using Amazon.ECS;
using Amazon.ECS.Model;
using Amazon.Runtime;
using Amazon.SQS;
using Amazon.SQS.Model;
using Microsoft.Extensions.Logging;
using Sentinel.Contracts.Observations;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Contracts.Queries;
using Sentinel.Ingestion.Application.Abstractions.Providers;
using KeyValuePair = Amazon.ECS.Model.KeyValuePair;

namespace Sentinel.Ingestion.Infrastructure.Providers;

/// <summary>
/// Reads the dependencies the AWS environment's workloads declare, as topology observations (Phase E01, Stage 2).
/// An ECS service in the configured clusters depends on what the task definition it serves names in its environment: a
/// service it calls through service discovery, <c>services__{name}__{endpoint}__{index}</c>, the convention the workloads
/// resolve their dependencies with, and an SQS queue of the account it is given by name or URL. A declared edge is what a
/// workload is configured to use, not what it was seen calling, so it carries less confidence than a traced call, and the
/// traces still report those. Only the name of the variable that declares an edge is recorded, never its value.
/// </summary>
internal sealed class AwsTopologyProvider(IAmazonECS ecs, IAmazonSQS sqs, AwsProviderOptions options, TimeProvider time, ILogger<AwsTopologyProvider> logger) : ITopologyProvider
{
    public const string ProviderName = "aws";

    private const double DeclaredConfidence = 0.8;

    public string Name => ProviderName;

    public async Task<ProviderResult<TopologyObservation>> GetDependenciesAsync(TopologyRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            DateTimeOffset now = time.GetUtcNow();
            HashSet<string> queues = await QueuesAsync(cancellationToken);
            var edges = new Dictionary<ObservationId, TopologyObservation>();
            foreach (string cluster in options.Clusters)
            {
                foreach (Service service in (await EcsServices.ListAsync(ecs, cluster, cancellationToken)).Where(service => service.Status == "ACTIVE"))
                {
                    if (!PrometheusMetricsProvider.TryServiceId(service.ServiceName, out ServiceId caller))
                    {
                        continue;
                    }

                    TaskDefinition definition = (await ecs.DescribeTaskDefinitionAsync(new DescribeTaskDefinitionRequest { TaskDefinition = service.TaskDefinition }, cancellationToken)).TaskDefinition;
                    DateTimeOffset since = service.CreatedAt is { } created ? new DateTimeOffset(DateTime.SpecifyKind(created, DateTimeKind.Utc)) : request.Range.From;
                    string resource = string.Create(CultureInfo.InvariantCulture, $"ecs/{cluster}/{service.ServiceName}/task-definitions/{definition.Family}:{definition.Revision}");
                    foreach (KeyValuePair variable in (definition.ContainerDefinitions ?? []).SelectMany(container => container.Environment ?? []))
                    {
                        if (DependencyOf(variable, queues) is not { } dependency || !PrometheusMetricsProvider.TryServiceId(dependency.Target, out ServiceId target) || target == caller)
                        {
                            continue;
                        }

                        TopologyObservation edge = Edge(request, now, (resource, since), variable.Name, caller, (target, dependency.Type, dependency.Protocol));
                        edges.TryAdd(edge.ObservationId, edge);
                    }
                }
            }

            return new ProviderResult<TopologyObservation>([.. edges.Values], request.Range.To, false, ProviderStatus.Healthy);
        }
        catch (Exception exception) when (exception is AmazonServiceException or AmazonClientException or HttpRequestException or TimeoutException || exception is TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(exception, "Reading the AWS topology failed");
            return ProviderResult<TopologyObservation>.Unavailable(request.Range.From, exception.Message);
        }
    }

    // A service-discovery entry names the service and the endpoint it is called on; a queue is named by its name or by
    // its URL, which ends in /{account}/{queue}.
    private static (string Target, string Type, string Protocol)? DependencyOf(KeyValuePair variable, HashSet<string> queues)
    {
        if ((variable.Name ?? string.Empty).Split("__") is ["services", { Length: > 0 } service, { Length: > 0 } endpoint, { } index]
            && int.TryParse(index, NumberStyles.None, CultureInfo.InvariantCulture, out _))
        {
            return (service, "calls", endpoint);
        }

        string value = variable.Value ?? string.Empty;
        if (queues.Contains(value))
        {
            return (value, "queue", "sqs");
        }

        return Uri.TryCreate(value, UriKind.Absolute, out Uri? url)
            && url.AbsolutePath.Trim('/').Split('/') is [{ } account, { } queue]
            && account.All(char.IsAsciiDigit)
            && queues.Contains(queue)
            ? (queue, "queue", "sqs")
            : null;
    }

    private async Task<HashSet<string>> QueuesAsync(CancellationToken cancellationToken)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        string? next = null;
        do
        {
            ListQueuesResponse page = await sqs.ListQueuesAsync(new ListQueuesRequest { MaxResults = 1000, NextToken = next }, cancellationToken);
            foreach (string url in page.QueueUrls ?? [])
            {
                names.Add(url.TrimEnd('/').Split('/')[^1]);
            }

            next = page.NextToken;
        }
        while (!string.IsNullOrEmpty(next));

        return names;
    }

    private static TopologyObservation Edge(
        TopologyRequest request,
        DateTimeOffset ingestedAt,
        (string Resource, DateTimeOffset Since) declaredBy,
        string variable,
        ServiceId caller,
        (ServiceId Target, string Type, string Protocol) dependency) => new()
        {
            ObservationId = ObservationId.From(string.Create(CultureInfo.InvariantCulture, $"{ProviderName}:dependency:{caller}:{dependency.Target}:{request.Range.To.ToUnixTimeMilliseconds() / 60_000}")),
            TenantId = request.TenantId,
            EnvironmentId = request.EnvironmentId,
            ServiceId = caller,
            SourceProvider = ProviderName,
            SourceResource = declaredBy.Resource,
            EventTime = request.Range.To,
            IngestTime = ingestedAt,
            TargetServiceId = dependency.Target,
            DependencyType = dependency.Type,
            Protocol = dependency.Protocol,
            Confidence = DeclaredConfidence,
            FirstSeen = declaredBy.Since < request.Range.To ? declaredBy.Since : request.Range.To,
            LastSeen = request.Range.To,
            Attributes = new Dictionary<string, string>(StringComparer.Ordinal) { ["declaredBy"] = variable }
        };
}
