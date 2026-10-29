using Sentinel.Contracts;
using Sentinel.Contracts.Incidents;
using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.Gateway.Application.Options;

public sealed class NotificationChannelOptions
{
    public string Name { get; set; } = string.Empty;

    /// <summary><c>log</c> or <c>webhook</c>.</summary>
    public string Kind { get; set; } = "log";

    public Uri? Url { get; set; }

    /// <summary>The configuration key that holds the webhook's signing secret, kept in user secrets or a secret manager.</summary>
    public string? SecretKey { get; set; }
}
