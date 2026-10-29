using Sentinel.Contracts;
using Sentinel.Contracts.Incidents;
using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.Api.Application.Live;

/// <summary>Configured as <c>Api:Notifications</c> (Phase 08 notification routing).</summary>
public sealed class NotificationOptions
{
    public const string Section = "Api:Notifications";

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

public sealed class NotificationChannelOptions
{
    public string Name { get; set; } = string.Empty;

    /// <summary><c>log</c> or <c>webhook</c>.</summary>
    public string Kind { get; set; } = "log";

    public Uri? Url { get; set; }

    /// <summary>The configuration key that holds the webhook's signing secret, kept in user secrets or a secret manager.</summary>
    public string? SecretKey { get; set; }
}

public sealed class NotificationRouteOptions
{
    /// <summary>A <see cref="LiveEventKinds"/> value.</summary>
    public string Event { get; set; } = string.Empty;

    /// <summary>For events that carry a severity: only this severe or worse.</summary>
    public Severity? MinimumSeverity { get; set; }

    public string Channel { get; set; } = string.Empty;

    /// <summary>Only this tenant's events, when set.</summary>
    public string? Tenant { get; set; }
}

/// <summary>One notification to send, kept until it is delivered or gives up.</summary>
public sealed record NotificationMessage(
    Guid NotificationId,
    string Channel,
    TenantId TenantId,
    IncidentId IncidentId,
    string Event,
    string Text,
    Uri? Link,
    DateTimeOffset OccurredAt);

public enum NotificationStatus
{
    Pending,
    Delivered,
    Failed
}

/// <summary>Api's notification outbox: enqueued in the inbox transaction, delivered by a dispatcher with retries.</summary>
public interface INotificationStore
{
    Task EnqueueAsync(NotificationMessage message, CancellationToken cancellationToken);

    Task<IReadOnlyList<(NotificationMessage Message, int Attempts)>> DueAsync(DateTimeOffset now, int limit, CancellationToken cancellationToken);

    Task MarkAsync(Guid notificationId, NotificationStatus status, int attempts, string? error, DateTimeOffset? nextAttemptAt, CancellationToken cancellationToken);
}

/// <summary>A notification provider: one way to reach people, behind Sentinel's own interface.</summary>
public interface INotificationProvider
{
    string Kind { get; }

    Task SendAsync(NotificationMessage message, NotificationChannelOptions channel, CancellationToken cancellationToken);
}

/// <summary>Decides which channels a live event notifies. Pure, so routing rules are testable on their own.</summary>
public static class NotificationRouter
{
    private static readonly Severity[] Order = [Severity.Low, Severity.Medium, Severity.High, Severity.Critical];

    public static IReadOnlyList<NotificationMessage> Route(LiveEventDraft draft, NotificationOptions options, Func<Guid> newId)
    {
        ArgumentNullException.ThrowIfNull(draft);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(newId);

        Uri? link = options.WebBaseAddress is { } web ? new Uri(web, $"/incidents/{draft.IncidentId}") : null;
        return [.. options.Routes
            .Where(route => route.Event == draft.Kind && SevereEnough(draft.Severity, route.MinimumSeverity) && (route.Tenant is null || route.Tenant == draft.TenantId.Value))
            .Select(route => route.Channel)
            .Where(channel => options.Channels.Any(configured => configured.Name == channel))
            .Distinct(StringComparer.Ordinal)
            .Select(channel => new NotificationMessage(newId(), channel, draft.TenantId, draft.IncidentId, draft.Kind, draft.Summary, link, draft.OccurredAt))];
    }

    private static bool SevereEnough(Severity? actual, Severity? minimum) =>
        minimum is null || actual is { } severity && Array.IndexOf(Order, severity) >= Array.IndexOf(Order, minimum.Value);
}
