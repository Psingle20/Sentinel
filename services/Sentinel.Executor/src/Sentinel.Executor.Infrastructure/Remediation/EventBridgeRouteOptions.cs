using Amazon.Runtime;
using Microsoft.Extensions.Configuration;

namespace Sentinel.Executor.Infrastructure.Remediation;

/// <summary>
/// Where Executor restores event routes, from <c>Executor:Adapters:EventBridge</c> (Phase E01, Stage 4): the endpoint, never
/// AWS's own unless configured, the region, the account the access key selects, and the routes it may restore, each rule
/// with its approved pattern. The approved patterns are the routes' source of truth, as a versioned definition would be;
/// an action names only the rule. The credentials are Executor's alone.
/// </summary>
internal sealed class EventBridgeRouteOptions
{
    public const string Section = "Executor:Adapters:EventBridge";

    public required Uri ServiceUrl { get; init; }

    public string Region { get; init; } = "us-east-1";

    public required string AccessKey { get; init; }

    public required string SecretKey { get; init; }

    /// <summary>The approved pattern of each rule Executor may restore, by rule name.</summary>
    public IReadOnlyDictionary<string, string> Routes { get; init; } = new Dictionary<string, string>();

    public AWSCredentials Credentials() => new BasicAWSCredentials(AccessKey, SecretKey);

    public static bool Configured(IConfiguration configuration) =>
        configuration.GetSection(Section)["ServiceUrl"] is { Length: > 0 };

    public static EventBridgeRouteOptions From(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        IConfigurationSection section = configuration.GetSection(Section);
        return new EventBridgeRouteOptions
        {
            ServiceUrl = new Uri(section["ServiceUrl"] ?? throw new InvalidOperationException($"{Section}:ServiceUrl is not configured.")),
            Region = section["Region"] ?? "us-east-1",
            AccessKey = section["AccessKey"] is { Length: > 0 } key ? key : throw new InvalidOperationException($"{Section}:AccessKey is not configured. Supply it through secret configuration or the orchestrator."),
            SecretKey = section["SecretKey"] is { Length: > 0 } secret ? secret : throw new InvalidOperationException($"{Section}:SecretKey is not configured. Supply it through secret configuration or the orchestrator."),
            Routes = section.GetSection("Routes").GetChildren().Where(route => !string.IsNullOrWhiteSpace(route.Value)).ToDictionary(route => route.Key, route => route.Value!, StringComparer.Ordinal)
        };
    }
}
