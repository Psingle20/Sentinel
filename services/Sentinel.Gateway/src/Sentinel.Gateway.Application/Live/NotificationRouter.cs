using Sentinel.Contracts;
using Sentinel.Contracts.Incidents;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Gateway.Application.Options;

namespace Sentinel.Gateway.Application.Live;

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
