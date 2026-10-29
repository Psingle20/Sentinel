using Sentinel.Contracts;
using Sentinel.Contracts.Incidents;
using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.Gateway.Application.Options;

/// <summary>Configured as <c>Gateway:Notifications</c> (Phase 08 notification routing).</summary>
public sealed class NotificationOptions
{
    public const string Section = "Gateway:Notifications";

    /// <summary>Where notifications can go: a channel names a provider kind and its target.</summary>
    public IList<NotificationChannelOptions> Channels { get; } = [];

    /// <summary>Which live events notify which channel.</summary>
    public IList<NotificationRouteOptions> Routes { get; } = [];

    /// <summary>The web app's address, for links in notifications.</summary>
    public Uri? WebBaseAddress { get; set; }

    public int MaxAttempts { get; set; } = 5;

    /// <summary>Hosts a webhook channel may target; deployment configuration only, so the control plane cannot widen it.</summary>
    public IList<string> AllowedWebhookHosts { get; } = [];
}
