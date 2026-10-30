using Amazon.Runtime;
using Microsoft.Extensions.Configuration;

namespace Sentinel.Ingestion.Infrastructure.Providers;

/// <summary>
/// Where Ingestion reads the AWS-emulated environment, from <c>Ingestion:Providers:Aws</c> (Phase E01, Stage 2): the
/// endpoint, the region, the read credentials and the ECS clusters to watch. Its ECS deployments, the topology its ECS
/// services declare and its SQS queues' attributes are all read with them. Ingestion is the only holder of provider
/// credentials (ADR-007).
/// </summary>
internal sealed class AwsProviderOptions
{
    public const string Section = "Ingestion:Providers:Aws";

    public required Uri ServiceUrl { get; init; }

    public string Region { get; init; } = "us-east-1";

    public required string AccessKey { get; init; }

    public required string SecretKey { get; init; }

    public IReadOnlyList<string> Clusters { get; init; } = [];

    public AWSCredentials Credentials() => new BasicAWSCredentials(AccessKey, SecretKey);

    public static bool Configured(IConfiguration configuration) =>
        configuration.GetSection(Section)["ServiceUrl"] is { Length: > 0 };

    public static AwsProviderOptions From(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        IConfigurationSection section = configuration.GetSection(Section);
        return new AwsProviderOptions
        {
            ServiceUrl = new Uri(section["ServiceUrl"] ?? throw new InvalidOperationException($"{Section}:ServiceUrl is not configured.")),
            Region = section["Region"] ?? "us-east-1",
            AccessKey = section["AccessKey"] is { Length: > 0 } key ? key : throw new InvalidOperationException($"{Section}:AccessKey is not configured. Set it through local secret configuration or the orchestrator."),
            SecretKey = section["SecretKey"] is { Length: > 0 } secret ? secret : throw new InvalidOperationException($"{Section}:SecretKey is not configured. Set it through local secret configuration or the orchestrator."),
            Clusters = [.. section.GetSection("Clusters").GetChildren().Select(cluster => cluster.Value).OfType<string>()]
        };
    }
}
