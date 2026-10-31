using System.Diagnostics;
using System.Text.Json;
using Amazon.CloudWatch;
using Amazon.CloudWatchLogs;
using Amazon.ECS;
using Amazon.EventBridge;
using Amazon.IdentityManagement;
using Amazon.Runtime;
using Amazon.SecurityToken;
using Amazon.SQS;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using DotNet.Testcontainers.Networks;

namespace Reference.Tests.Aws;

/// <summary>
/// The pinned local AWS of ADR-021, started for the environment conformance tests: Floci on a network of its own,
/// with the Docker socket mounted so ECS tasks run as real containers on that network. Every container local AWS
/// starts carries this instance's label, so a test can prove that nothing it started outlives it.
/// </summary>
public sealed class LocalAws : IAsyncLifetime
{
    public const string DefaultAccount = "000000000000";
    public const string Region = "us-east-1";

    // Synthetic and never validated: local AWS does not check signatures, and the access key only selects the account.
    private const string Secret = "test-not-a-secret";
    private const string LabelKey = "sentinel.reference.aws";

    private INetwork _network = null!;
    private IContainer _container = null!;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static LocalAwsPin Pin { get; } = JsonSerializer.Deserialize<LocalAwsPin>(
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Aws", "local-aws.json")), Json)!;

    /// <summary>The value of the label on every container this instance and the tasks it runs start.</summary>
    public string RunId { get; } = Guid.NewGuid().ToString("N")[..12];

    public string NetworkName => _network.Name;

    public Uri Endpoint { get; private set; } = null!;

    public TimeSpan StartedIn { get; private set; }

    public async Task InitializeAsync()
    {
        var started = Stopwatch.StartNew();
        _network = new NetworkBuilder().WithName($"reference-aws-{RunId}").Build();
        await _network.CreateAsync();
        _container = new ContainerBuilder($"{Pin.Image}@{Pin.Digest}")
            .WithNetwork(_network)
            .WithNetworkAliases("local-aws")
            .WithPortBinding(Pin.Port, true)
            .WithBindMount("/var/run/docker.sock", "/var/run/docker.sock")
            .WithCreateParameterModifier(parameters => parameters.User = "root")
            .WithLabel(LabelKey, RunId)
            .WithEnvironment("FLOCI_HOSTNAME", "local-aws")
            .WithEnvironment("FLOCI_SERVICES_ECS_DOCKER_NETWORK", _network.Name)
            .WithEnvironment("FLOCI_SERVICES_ECS_IMAGE_PULL_BEHAVIOR", "prefer-cached")
            .WithEnvironment("FLOCI_DOCKER_EXTRA_LABELS_0__KEY", LabelKey)
            .WithEnvironment("FLOCI_DOCKER_EXTRA_LABELS_0__VALUE", RunId)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(request => request.ForPort((ushort)Pin.Port).ForPath("/_localstack/health")))
            .Build();
        await _container.StartAsync();
        Endpoint = new Uri($"http://{_container.Hostname}:{_container.GetMappedPublicPort(Pin.Port)}");
        StartedIn = started.Elapsed;
    }

    /// <summary>Stops local AWS as a developer's Ctrl+C would, leaving whatever it started for the caller to inspect.</summary>
    public Task StopAsync() => _container.StopAsync();

    public async Task DisposeAsync()
    {
        await _container.DisposeAsync();
        await RemoveStartedContainersAsync();
        await _network.DisposeAsync();
    }

    /// <summary>What local AWS says about its services, from its LocalStack-compatible health endpoint.</summary>
    public async Task<JsonDocument> HealthAsync()
    {
        using HttpClient http = new() { BaseAddress = Endpoint };
        return JsonDocument.Parse(await http.GetStringAsync(new Uri("/_localstack/health", UriKind.Relative)));
    }

    public Task<string> ContainerLogsAsync() => _container.GetLogsAsync().ContinueWith(task => task.Result.Stdout + task.Result.Stderr, TaskScheduler.Default);

    public AmazonSQSClient Sqs(string account = DefaultAccount, string region = Region) =>
        new(Credentials(account), new AmazonSQSConfig { ServiceURL = Endpoint.ToString(), AuthenticationRegion = region });

    public AmazonECSClient Ecs(string account = DefaultAccount) =>
        new(Credentials(account), new AmazonECSConfig { ServiceURL = Endpoint.ToString(), AuthenticationRegion = Region });

    public AmazonEventBridgeClient Events(string account = DefaultAccount) =>
        new(Credentials(account), new AmazonEventBridgeConfig { ServiceURL = Endpoint.ToString(), AuthenticationRegion = Region });

    public AmazonIdentityManagementServiceClient Iam(string account = DefaultAccount) =>
        new(Credentials(account), new AmazonIdentityManagementServiceConfig { ServiceURL = Endpoint.ToString(), AuthenticationRegion = Region });

    public AmazonSecurityTokenServiceClient Sts(AWSCredentials credentials) =>
        new(credentials, new AmazonSecurityTokenServiceConfig { ServiceURL = Endpoint.ToString(), AuthenticationRegion = Region });

    public AmazonSecurityTokenServiceClient Sts(string account = DefaultAccount) => Sts(Credentials(account));

    public AmazonCloudWatchLogsClient CloudWatchLogs(string account = DefaultAccount) =>
        new(Credentials(account), new AmazonCloudWatchLogsConfig { ServiceURL = Endpoint.ToString(), AuthenticationRegion = Region });

    public AmazonCloudWatchClient Metrics(string account = DefaultAccount) =>
        new(Credentials(account), new AmazonCloudWatchConfig { ServiceURL = Endpoint.ToString(), AuthenticationRegion = Region });

    /// <summary>The containers local AWS started for this instance that still exist, running or not.</summary>
    public Task<IReadOnlyList<string>> StartedContainersAsync() => DockerAsync("ps", "-a", "-q", "--filter", $"label={LabelKey}={RunId}");

    private async Task RemoveStartedContainersAsync()
    {
        foreach (string container in await StartedContainersAsync())
        {
            await DockerAsync("rm", "-f", container);
        }
    }

    private static BasicAWSCredentials Credentials(string account) => new(account, Secret);

    private static async Task<IReadOnlyList<string>> DockerAsync(params string[] arguments)
    {
        using Process docker = Process.Start(new ProcessStartInfo("docker", arguments) { RedirectStandardOutput = true, RedirectStandardError = true })
            ?? throw new InvalidOperationException("docker could not be started.");
        string output = await docker.StandardOutput.ReadToEndAsync();
        await docker.WaitForExitAsync();
        return [.. output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];
    }
}
