using System.Diagnostics;
using Amazon.ECS;
using Amazon.ECS.Model;
using Microsoft.Extensions.Logging.Abstractions;
using Sentinel.Contracts;
using Sentinel.Contracts.Observations;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Contracts.Queries;
using Sentinel.Ingestion.Application.Abstractions.Providers;
using Sentinel.Ingestion.Infrastructure.Providers;
using Shouldly;
using Task = System.Threading.Tasks.Task;

namespace Sentinel.Ingestion.Tests;

/// <summary>
/// The ECS deployment provider against the pinned emulator (Phase E01, Stage 2): a service's creation is a create, a
/// rollout to a new task definition a deploy and a rollout back a rollback, each named by its image's tag with the
/// version it replaced, and each completion follows its start; an emulator that does not answer is an unavailable
/// result, never an exception.
/// </summary>
[Collection(AwsEmulatorCollection.Name)]
[Trait("Category", "Emulator")]
public sealed class EcsDeploymentProviderTests(AwsEmulator emulator)
{
    private const string Repository = "sentinel-ingestion-ecs-test";

    private static readonly TenantId Tenant = TenantId.From("acme");
    private static readonly EnvironmentId Environment = EnvironmentId.From("reference");

    [Fact]
    public async Task Creation_rollout_and_rollback_are_create_deploy_and_rollback_with_their_versions()
    {
        await AwsEmulator.TagAsync("redis:7-alpine", $"{Repository}:v1.4.0");
        await AwsEmulator.TagAsync("redis:7-alpine", $"{Repository}:v1.4.1");
        using AmazonECSClient ecs = emulator.Ecs();
        string cluster = $"ingestion-{Guid.NewGuid().ToString("N")[..8]}";
        await ecs.CreateClusterAsync(new CreateClusterRequest { ClusterName = cluster });
        string baseline = await RegisterAsync(ecs, "v1.4.0");
        string next = await RegisterAsync(ecs, "v1.4.1");
        EcsDeploymentProvider provider = Provider(ecs, cluster);
        DateTimeOffset from = DateTimeOffset.UtcNow.AddMinutes(-1);

        await ecs.CreateServiceAsync(new CreateServiceRequest { Cluster = cluster, ServiceName = "queue-consumer", TaskDefinition = baseline, DesiredCount = 1 });
        DeploymentObservation created = await UntilAsync(provider, from, observations => observations.FirstOrDefault(observation => observation.Status == "completed" && observation.Version == "v1.4.0"));
        created.ChangeKind.ShouldBe("create", "a service's first deployment is its creation");
        created.ServiceId.ShouldBe(ServiceId.From("queue-consumer"));
        created.PreviousVersion.ShouldBeNull();

        await ecs.UpdateServiceAsync(new UpdateServiceRequest { Cluster = cluster, Service = "queue-consumer", TaskDefinition = next });
        DeploymentObservation deployed = await UntilAsync(provider, from, observations => observations.FirstOrDefault(observation => observation.Status == "completed" && observation.Version == "v1.4.1"));
        deployed.ChangeKind.ShouldBe("deploy");
        deployed.PreviousVersion.ShouldBe("v1.4.0");
        deployed.CompletedAt.ShouldNotBeNull().ShouldBeGreaterThanOrEqualTo(deployed.StartedAt);
        ProviderResult<DeploymentObservation> both = await provider.GetDeploymentsAsync(Request(from), CancellationToken.None);
        both.Observations.ShouldContain(observation => observation.DeploymentId == deployed.DeploymentId && observation.Status == "in-progress", "the rollout's start is reported too");
        both.Observations.Select(observation => observation.ObservationId).ShouldBeUnique();

        await ecs.UpdateServiceAsync(new UpdateServiceRequest { Cluster = cluster, Service = "queue-consumer", TaskDefinition = baseline });
        DeploymentObservation rolledBack = await UntilAsync(provider, from, observations => observations.FirstOrDefault(observation => observation.Status == "completed" && observation.ChangeKind == "rollback"));
        rolledBack.Version.ShouldBe("v1.4.0");
        rolledBack.PreviousVersion.ShouldBe("v1.4.1");

        await ecs.UpdateServiceAsync(new UpdateServiceRequest { Cluster = cluster, Service = "queue-consumer", DesiredCount = 0 });
    }

    [Fact]
    public async Task An_emulator_that_does_not_answer_is_an_unavailable_result()
    {
        using var ecs = new AmazonECSClient(
            new Amazon.Runtime.BasicAWSCredentials(AwsEmulator.Account, AwsEmulator.Secret),
            new AmazonECSConfig { ServiceURL = "http://127.0.0.1:9", AuthenticationRegion = AwsEmulator.Region, MaxErrorRetry = 0, Timeout = TimeSpan.FromSeconds(2) });
        EcsDeploymentProvider provider = Provider(ecs, "reference");

        ProviderResult<DeploymentObservation> result = await provider.GetDeploymentsAsync(Request(DateTimeOffset.UtcNow.AddMinutes(-1)), CancellationToken.None);

        result.Status.ShouldBe(ProviderStatus.Unavailable);
        result.Observations.ShouldBeEmpty();
    }

    private static async Task<string> RegisterAsync(AmazonECSClient ecs, string version) =>
        (await ecs.RegisterTaskDefinitionAsync(new RegisterTaskDefinitionRequest
        {
            Family = "queue-consumer",
            ContainerDefinitions = [new ContainerDefinition { Name = "consumer", Image = $"{Repository}:{version}", Essential = true, Memory = 64, Command = ["redis-server", "--save", "", "--appendonly", "no"] }]
        })).TaskDefinition.TaskDefinitionArn;

    private EcsDeploymentProvider Provider(AmazonECSClient ecs, string cluster) => new(
        ecs,
        new AwsProviderOptions { ServiceUrl = emulator.Endpoint, AccessKey = AwsEmulator.Account, SecretKey = AwsEmulator.Secret, Clusters = [cluster] },
        TimeProvider.System,
        NullLogger<EcsDeploymentProvider>.Instance);

    private static DeploymentRequest Request(DateTimeOffset from) => new(Tenant, Environment, new TimeRange(from, DateTimeOffset.UtcNow.AddMinutes(5)));

    // Pulls as Ingestion's loop does until the expected observation appears.
    private static async Task<DeploymentObservation> UntilAsync(EcsDeploymentProvider provider, DateTimeOffset from, Func<IReadOnlyList<DeploymentObservation>, DeploymentObservation?> find)
    {
        var deadline = Stopwatch.StartNew();
        while (deadline.Elapsed < TimeSpan.FromMinutes(2))
        {
            ProviderResult<DeploymentObservation> result = await provider.GetDeploymentsAsync(Request(from), CancellationToken.None);
            result.Status.ShouldBe(ProviderStatus.Healthy, result.Error);
            if (find(result.Observations) is { } found)
            {
                return found;
            }

            await Task.Delay(TimeSpan.FromSeconds(2));
        }

        throw new TimeoutException("The expected deployment observation did not appear within two minutes.");
    }
}
