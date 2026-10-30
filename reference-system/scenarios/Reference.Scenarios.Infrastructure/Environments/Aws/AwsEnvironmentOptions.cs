using Amazon.Runtime;
using Microsoft.Extensions.Configuration;

namespace Reference.Scenarios.Infrastructure.Environments.Aws;

/// <summary>
/// The AWS-emulated environment as the AppHost describes it under <c>Reference:Aws</c>: the emulator's endpoint as the
/// runner reaches it, the account and region the scenarios run in, the consumer's ECS service and image, and the emulator
/// and digest a run record names. Containers the emulator starts reach the host's endpoints through
/// <see cref="ContainerHost"/>, so every address handed to a task is rewritten from localhost to it. The secret is
/// synthetic: the emulator does not check signatures, and the access key only selects the account.
/// </summary>
public sealed class AwsEnvironmentOptions
{
    public const string Section = "Reference:Aws";

    private const string Secret = "test-not-a-secret";

    public required Uri ServiceUrl { get; init; }

    public string Region { get; init; } = "us-east-1";

    public string Account { get; init; } = "000000000000";

    public string Cluster { get; init; } = "reference";

    public string Service { get; init; } = "queue-consumer";

    public string Queue { get; init; } = "orders";

    public string DeadLetterQueue { get; init; } = "orders-dlq";

    public string Image { get; init; } = "reference-queue-consumer:dev";

    public string BaselineRevision { get; init; } = "v1.4.0";

    public IReadOnlyList<string> Revisions { get; init; } = ["v1.4.0", "v1.4.1"];

    public string ContainerHost { get; init; } = "host.docker.internal";

    public string? Emulator { get; init; }

    public string? ImageDigest { get; init; }

    /// <summary>The environment every consumer task starts with, apart from its revision.</summary>
    public IReadOnlyDictionary<string, string> TaskEnvironment { get; init; } = new Dictionary<string, string>();

    public AWSCredentials Credentials() => new BasicAWSCredentials(Account, Secret);

    /// <summary>The options from configuration, with the task environment built from the runner's own telemetry and order-service addresses.</summary>
    public static AwsEnvironmentOptions From(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        IConfigurationSection section = configuration.GetSection(Section);
        Uri endpoint = new(section["ServiceUrl"] ?? throw new InvalidOperationException($"{Section}:ServiceUrl must name the emulator's endpoint."));
        string host = section["ContainerHost"] ?? "host.docker.internal";
        string account = section["Account"] ?? "000000000000";
        string region = section["Region"] ?? "us-east-1";

        var task = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Reference__Queue__Provider"] = "sqs",
            ["Reference__Queue__Sqs__ServiceUrl"] = ForContainers(endpoint.ToString(), host),
            ["Reference__Queue__Sqs__Region"] = region,
            ["Reference__Queue__Sqs__AccessKey"] = account
        };
        Add("Reference__Telemetry__MetricsEndpoint", configuration["Reference:Telemetry:MetricsEndpoint"]);
        Add("Reference__Telemetry__LogsEndpoint", configuration["Reference:Telemetry:LogsEndpoint"]);
        Add("Reference__Telemetry__TracesEndpoint", configuration["Reference:Telemetry:TracesEndpoint"]);
        Add("services__order-service__http__0", configuration["services:order-service:http:0"]);

        return new AwsEnvironmentOptions
        {
            ServiceUrl = endpoint,
            Region = region,
            Account = account,
            Image = section["Image"] ?? "reference-queue-consumer:dev",
            ContainerHost = host,
            Emulator = section["Emulator"],
            ImageDigest = section["ImageDigest"],
            TaskEnvironment = task
        };

        void Add(string name, string? address)
        {
            if (address is not null)
            {
                task[name] = ForContainers(address, host);
            }
        }
    }

    /// <summary>An address on this host as a container sees it: localhost becomes the container host.</summary>
    public static string ForContainers(string address, string host)
    {
        var uri = new UriBuilder(address);
        if (uri.Host is "localhost" or "127.0.0.1" or "[::1]")
        {
            uri.Host = host;
        }

        return uri.Uri.ToString().TrimEnd('/');
    }
}
