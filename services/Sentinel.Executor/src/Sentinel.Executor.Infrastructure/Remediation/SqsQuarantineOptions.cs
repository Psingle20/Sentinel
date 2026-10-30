using Amazon.Runtime;
using Microsoft.Extensions.Configuration;

namespace Sentinel.Executor.Infrastructure.Remediation;

/// <summary>
/// Where Executor quarantines SQS messages, from <c>Executor:Adapters:Sqs</c> (Phase E01, Stage 2): the endpoint, never
/// AWS's own unless configured, the region, the account the access key selects, and the queues it may touch. Each queue's
/// dead-letter queue is named with <see cref="DeadLetterSuffix"/>. The credentials are Executor's alone.
/// </summary>
internal sealed class SqsQuarantineOptions
{
    public const string Section = "Executor:Adapters:Sqs";

    public required Uri ServiceUrl { get; init; }

    public string Region { get; init; } = "us-east-1";

    public required string AccessKey { get; init; }

    public required string SecretKey { get; init; }

    public IReadOnlyList<string> Queues { get; init; } = [];

    public string DeadLetterSuffix { get; init; } = "-dlq";

    /// <summary>How long the redrive window stays open, at most, for the poison message's next receive.</summary>
    public TimeSpan Window { get; init; } = TimeSpan.FromSeconds(30);

    public AWSCredentials Credentials() => new BasicAWSCredentials(AccessKey, SecretKey);

    public static SqsQuarantineOptions From(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        IConfigurationSection section = configuration.GetSection(Section);
        return new SqsQuarantineOptions
        {
            ServiceUrl = new Uri(section["ServiceUrl"] ?? throw new InvalidOperationException($"{Section}:ServiceUrl is not configured.")),
            Region = section["Region"] ?? "us-east-1",
            AccessKey = section["AccessKey"] is { Length: > 0 } key ? key : throw new InvalidOperationException($"{Section}:AccessKey is not configured. Supply it through secret configuration or the orchestrator."),
            SecretKey = section["SecretKey"] is { Length: > 0 } secret ? secret : throw new InvalidOperationException($"{Section}:SecretKey is not configured. Supply it through secret configuration or the orchestrator."),
            Queues = [.. section.GetSection("Queues").GetChildren().Select(queue => queue.Value).OfType<string>()],
            Window = TimeSpan.TryParse(section["Window"], System.Globalization.CultureInfo.InvariantCulture, out TimeSpan window) ? window : TimeSpan.FromSeconds(30)
        };
    }
}
