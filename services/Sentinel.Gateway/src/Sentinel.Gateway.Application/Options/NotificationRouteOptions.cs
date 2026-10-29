using Sentinel.Contracts;
using Sentinel.Contracts.Incidents;
using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.Gateway.Application.Options;

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
