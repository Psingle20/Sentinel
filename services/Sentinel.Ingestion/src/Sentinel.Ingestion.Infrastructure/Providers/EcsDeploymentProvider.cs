using System.Collections.Concurrent;
using System.Globalization;
using Amazon.ECS;
using Amazon.ECS.Model;
using Amazon.Runtime;
using Microsoft.Extensions.Logging;
using Sentinel.Contracts.Observations;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Contracts.Queries;
using Sentinel.Ingestion.Application.Abstractions.Providers;
using Sentinel.Ingestion.Domain.Observations;

namespace Sentinel.Ingestion.Infrastructure.Providers;

/// <summary>
/// Reads ECS service deployments as deployment observations (Phase E01, Stage 2). Each task-definition rollout of a
/// service in the configured clusters yields a <c>started</c> observation at its creation and, once ECS reports it
/// completed or failed, a <c>completed</c> or <c>failed</c> one. The version is the task's image tag; the previous version
/// is the task definition the service was last seen serving, else the family's previous revision. A service's first
/// deployment is its creation, kind <c>create</c>, which the deploy-blame rules ignore. ECS keeps only the current
/// deployment, so this provider remembers what it saw; and the emulator never moves a deployment's update time, so a
/// completion is timed when this provider first saw it.
/// </summary>
internal sealed class EcsDeploymentProvider(IAmazonECS ecs, AwsProviderOptions options, TimeProvider time, ILogger<EcsDeploymentProvider> logger) : IDeploymentProvider
{
    public const string ProviderName = "ecs";

    // A service's own creation and its first deployment are stamped within this of each other.
    private static readonly TimeSpan CreationTolerance = TimeSpan.FromSeconds(2);

    private readonly ConcurrentDictionary<string, string> _serving = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, string?> _previous = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, DateTimeOffset> _ended = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, TaskDefinitionView?> _definitions = new(StringComparer.Ordinal);

    public string Name => ProviderName;

    public async Task<ProviderResult<DeploymentObservation>> GetDeploymentsAsync(DeploymentRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            DateTimeOffset now = time.GetUtcNow();
            var observations = new List<DeploymentObservation>();
            foreach (string cluster in options.Clusters)
            {
                foreach (Service service in await EcsServices.ListAsync(ecs, cluster, cancellationToken))
                {
                    observations.AddRange(await TranslateAsync(cluster, service, request, now, cancellationToken));
                }
            }

            return new ProviderResult<DeploymentObservation>(observations, request.Range.To, false, ProviderStatus.Healthy);
        }
        catch (Exception exception) when (exception is AmazonServiceException or AmazonClientException or HttpRequestException or TimeoutException || exception is TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(exception, "Reading ECS deployments failed");
            return ProviderResult<DeploymentObservation>.Unavailable(request.Range.From, exception.Message);
        }
    }

    private async Task<List<DeploymentObservation>> TranslateAsync(string cluster, Service service, DeploymentRequest request, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var observations = new List<DeploymentObservation>();
        foreach (Deployment deployment in (service.Deployments ?? []).Where(deployment => deployment.Status == "PRIMARY" && deployment.CreatedAt is not null))
        {
            DateTimeOffset created = Utc(deployment.CreatedAt!.Value);
            bool creation = service.CreatedAt is { } serviceCreated && (created - Utc(serviceCreated)).Duration() <= CreationTolerance;
            string? previousArn = creation ? null : _previous.GetOrAdd(deployment.Id, _ => PreviousOf(service, deployment));
            TaskDefinitionView? current = await DefinitionAsync(deployment.TaskDefinition, cancellationToken);
            TaskDefinitionView? previous = previousArn is null ? null : await DefinitionAsync(previousArn, cancellationToken);
            string kind = KindOf(creation, current, previous);
            string? ended = EndOf(deployment.RolloutState);
            if (ended is not null)
            {
                _serving[ServiceKey(cluster, service)] = deployment.TaskDefinition;
            }

            DateTimeOffset? endedAt = ended is null ? null : _ended.GetOrAdd(deployment.Id, now);
            var record = new Record(deployment.Id, service.ServiceName, current?.Version ?? deployment.TaskDefinition, previous?.Version, kind, created, endedAt, $"ecs/{cluster}/{service.ServiceName}/deployments/{deployment.Id}");
            if (created >= request.Range.From && created < request.Range.To)
            {
                observations.Add(Observation(record, "started", created, "in-progress", completedAt: null, request, now));
            }

            if (ended is not null && endedAt >= request.Range.From && endedAt < request.Range.To)
            {
                observations.Add(Observation(record, ended, endedAt.Value, ended, endedAt, request, now));
            }
        }

        return observations;
    }

    private static string KindOf(bool creation, TaskDefinitionView? current, TaskDefinitionView? previous)
    {
        if (creation)
        {
            return "create";
        }

        return previous is not null && current is not null && current.Revision < previous.Revision ? "rollback" : "deploy";
    }

    private static string? EndOf(DeploymentRolloutState? state)
    {
        if (state == DeploymentRolloutState.COMPLETED)
        {
            return "completed";
        }

        return state == DeploymentRolloutState.FAILED ? "failed" : null;
    }

    // What the service served before this deployment: as last seen, else the family's previous revision.
    private string? PreviousOf(Service service, Deployment deployment)
    {
        if (_serving.TryGetValue(ServiceKey(service.ClusterArn, service), out string? serving) && serving != deployment.TaskDefinition)
        {
            return serving;
        }

        int colon = deployment.TaskDefinition.LastIndexOf(':');
        return colon > 0 && int.TryParse(deployment.TaskDefinition[(colon + 1)..], NumberStyles.Integer, CultureInfo.InvariantCulture, out int revision) && revision > 1
            ? string.Create(CultureInfo.InvariantCulture, $"{deployment.TaskDefinition[..colon]}:{revision - 1}")
            : null;
    }

    private async Task<TaskDefinitionView?> DefinitionAsync(string arn, CancellationToken cancellationToken)
    {
        if (_definitions.TryGetValue(arn, out TaskDefinitionView? known))
        {
            return known;
        }

        try
        {
            TaskDefinition definition = (await ecs.DescribeTaskDefinitionAsync(new DescribeTaskDefinitionRequest { TaskDefinition = arn }, cancellationToken)).TaskDefinition;
            string image = definition.ContainerDefinitions is [ContainerDefinition first, ..] ? first.Image : string.Empty;
            return _definitions[arn] = new TaskDefinitionView(definition.Revision ?? 0, VersionOf(image) ?? string.Create(CultureInfo.InvariantCulture, $"{definition.Family}:{definition.Revision}"));
        }
        catch (ClientException)
        {
            // A revision that no longer exists has no version to report.
            return _definitions[arn] = null;
        }
    }

    // The tag of an image such as registry/name:v1.4.1; an image pinned by digest or untagged has none.
    private static string? VersionOf(string image)
    {
        if (image.Contains('@', StringComparison.Ordinal))
        {
            return null;
        }

        int colon = image.LastIndexOf(':');
        return colon > image.LastIndexOf('/') ? image[(colon + 1)..] : null;
    }

    private static string ServiceKey(string cluster, Service service) => $"{cluster.Split('/')[^1]}/{service.ServiceName}";

    private static DateTimeOffset Utc(DateTime value) => new(DateTime.SpecifyKind(value, DateTimeKind.Utc));

    private static DeploymentObservation Observation(Record record, string state, DateTimeOffset eventTime, string status, DateTimeOffset? completedAt, DeploymentRequest request, DateTimeOffset ingestedAt) => new()
    {
        ObservationId = ObservationIdentity.Deployment(ProviderName, record.DeploymentId, state),
        TenantId = request.TenantId,
        EnvironmentId = request.EnvironmentId,
        ServiceId = ServiceId.From(record.Service),
        SourceProvider = ProviderName,
        SourceResource = record.Resource,
        EventTime = eventTime,
        IngestTime = ingestedAt,
        DeploymentId = record.DeploymentId,
        Version = record.Version,
        PreviousVersion = record.PreviousVersion,
        Actor = ProviderName,
        StartedAt = record.StartedAt,
        CompletedAt = completedAt,
        ChangeKind = record.Kind,
        Status = status
    };

    private sealed record Record(string DeploymentId, string Service, string Version, string? PreviousVersion, string Kind, DateTimeOffset StartedAt, DateTimeOffset? EndedAt, string Resource);

    private sealed record TaskDefinitionView(long Revision, string Version);
}
