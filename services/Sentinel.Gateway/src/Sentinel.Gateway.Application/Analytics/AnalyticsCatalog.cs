using CleanArchitecture.BuildingBlocks;
using Sentinel.Gateway.Domain.Approvals;

namespace Sentinel.Gateway.Application.Analytics;

/// <summary>
/// Product analytics limited to approved, privacy-safe events (Phase 08 deliverable 6). Only events
/// in this catalog are accepted, each with a fixed set of properties whose values come from closed
/// lists. Nothing that identifies a person, an incident or a tenant can be recorded, and no free
/// text: Api keeps daily counts per event and property values, never individual events.
/// Changing the catalog is a reviewed code change; docs/business-contracts/product-analytics.md describes it.
/// </summary>
public static class AnalyticsCatalog
{
    private static readonly string[] Statuses = ["detected", "investigating", "awaitingApproval", "remediating", "verifying", "resolved", "escalated"];
    private static readonly string[] Severities = ["critical", "high", "medium", "low"];
    private static readonly string[] Areas = ["overview", "policy", "runtime", "models", "sources", "api"];

    public static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, IReadOnlyList<string>>> Events =
        new Dictionary<string, IReadOnlyDictionary<string, IReadOnlyList<string>>>(StringComparer.Ordinal)
        {
            ["incident.viewed"] = Properties(("status", Statuses), ("severity", Severities)),
            ["approval.dialog-opened"] = Properties(("action", ["approve", "reject", "revoke"])),
            ["postmortem.viewed"] = Properties(("status", Statuses)),
            ["narrative.saved"] = Properties(("outcome", ["saved", "conflict", "failed"])),
            ["replay.requested"] = Properties(("outcome", ["reproduced", "differs", "failed"])),
            ["live.state-changed"] = Properties(("state", ["live", "reconnecting", "offline"])),
            ["admin.viewed"] = Properties(("area", Areas)),
            ["configuration.published"] = Properties(("area", Areas), ("outcome", ["published", "refused"]))
        };

    public static readonly Error UnknownEvent = Error.Problem("Analytics.UnknownEvent", "The event is not in the approved analytics catalog.");

    public static Error UnknownProperty(string name) => Error.Problem("Analytics.UnknownProperty", $"Property '{name}' is not approved for this event, or its value is not one of the allowed values.");

    /// <summary>The event's properties in a canonical order, or why it cannot be recorded.</summary>
    public static Result<string> Dimensions(string eventName, IReadOnlyDictionary<string, string>? properties)
    {
        if (!Events.TryGetValue(eventName, out IReadOnlyDictionary<string, IReadOnlyList<string>>? allowed))
        {
            return Result.Failure<string>(UnknownEvent);
        }

        properties ??= new Dictionary<string, string>();
        foreach ((string name, string value) in properties)
        {
            if (!allowed.TryGetValue(name, out IReadOnlyList<string>? values) || !values.Contains(value, StringComparer.Ordinal))
            {
                return Result.Failure<string>(UnknownProperty(name));
            }
        }

        return Result.Success(string.Join(";", properties.OrderBy(property => property.Key, StringComparer.Ordinal).Select(property => $"{property.Key}={property.Value}")));
    }

    private static Dictionary<string, IReadOnlyList<string>> Properties(params (string Name, string[] Values)[] properties) =>
        properties.ToDictionary(property => property.Name, property => (IReadOnlyList<string>)property.Values, StringComparer.Ordinal);
}
