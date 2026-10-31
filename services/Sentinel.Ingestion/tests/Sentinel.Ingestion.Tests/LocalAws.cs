using System.Diagnostics;
using System.Text.Json;
using Amazon.ECS;
using Amazon.EventBridge;
using Amazon.Runtime;
using Amazon.SQS;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using DotNet.Testcontainers.Networks;

namespace Sentinel.Ingestion.Tests;

/// <summary>
/// Local AWS ADR-021 pins, started once for the AWS providers' tests (Phase E01, Stage 2): with the Docker socket
/// mounted and a network of its own, so ECS tasks run as real containers, each labelled so that disposal removes them.
/// Its digest is read from the reference system's <c>local-aws.json</c>, the one place it is written.
/// </summary>
public sealed class LocalAws : IAsyncLifetime
{
    public const string Account = "000000000000";
    public const string Region = "us-east-1";

    // Synthetic and never validated: local AWS does not check signatures, and the access key only selects the account.
    public const string Secret = "test-not-a-secret";

    private const string LabelKey = "sentinel.ingestion.aws";

    private readonly string _runId = Guid.NewGuid().ToString("N")[..12];
    private INetwork _network = null!;
    private IContainer _container = null!;

    public Uri Endpoint { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        using var pin = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Aws", "local-aws.json")));
        string image = $"{pin.RootElement.GetProperty("image").GetString()}@{pin.RootElement.GetProperty("digest").GetString()}";
        int port = pin.RootElement.GetProperty("port").GetInt32();
        _network = new NetworkBuilder().WithName($"ingestion-aws-{_runId}").Build();
        await _network.CreateAsync();
        _container = new ContainerBuilder(image)
            .WithNetwork(_network)
            .WithNetworkAliases("local-aws")
            .WithPortBinding(port, true)
            .WithBindMount("/var/run/docker.sock", "/var/run/docker.sock")
            .WithCreateParameterModifier(parameters => parameters.User = "root")
            .WithEnvironment("FLOCI_HOSTNAME", "local-aws")
            .WithEnvironment("FLOCI_SERVICES_ECS_DOCKER_NETWORK", _network.Name)
            .WithEnvironment("FLOCI_SERVICES_ECS_IMAGE_PULL_BEHAVIOR", "prefer-cached")
            .WithEnvironment("FLOCI_DOCKER_EXTRA_LABELS_0__KEY", LabelKey)
            .WithEnvironment("FLOCI_DOCKER_EXTRA_LABELS_0__VALUE", _runId)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(request => request.ForPort((ushort)port).ForPath("/_localstack/health")))
            .Build();
        await _container.StartAsync();
        Endpoint = new Uri($"http://{_container.Hostname}:{_container.GetMappedPublicPort(port)}");
    }

    public async Task DisposeAsync()
    {
        await _container.DisposeAsync();
        foreach (string started in (await DockerAsync("ps", "-a", "-q", "--filter", $"label={LabelKey}={_runId}")).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            await DockerAsync("rm", "-f", started);
        }

        await _network.DisposeAsync();
    }

    public AmazonECSClient Ecs() => new(new BasicAWSCredentials(Account, Secret), new AmazonECSConfig { ServiceURL = Endpoint.ToString(), AuthenticationRegion = Region });

    public AmazonSQSClient Sqs() => new(new BasicAWSCredentials(Account, Secret), new AmazonSQSConfig { ServiceURL = Endpoint.ToString(), AuthenticationRegion = Region });

    public AmazonEventBridgeClient Events() => new(new BasicAWSCredentials(Account, Secret), new AmazonEventBridgeConfig { ServiceURL = Endpoint.ToString(), AuthenticationRegion = Region });

    /// <summary>Tags an image already local under another name, so a task definition names a version without a pull.</summary>
    public static Task TagAsync(string source, string target) => DockerAsync("tag", source, target);

    private static async Task<string> DockerAsync(params string[] arguments)
    {
        var start = new ProcessStartInfo("docker") { RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using Process docker = Process.Start(start)!;
        string output = await docker.StandardOutput.ReadToEndAsync();
        await docker.WaitForExitAsync();
        return output;
    }
}
