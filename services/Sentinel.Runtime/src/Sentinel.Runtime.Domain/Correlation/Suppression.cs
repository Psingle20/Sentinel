using System.Globalization;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Runtime.Domain.Incidents;

namespace Sentinel.Runtime.Domain.Correlation;

/// <summary>A maintenance window or known-noise rule: matching signals open nothing.</summary>
public sealed record Suppression(ServiceId? ServiceId, string? MetricName, DateTimeOffset From, DateTimeOffset Until, string Reason)
{
    public bool Matches(ServiceId service, string metric, DateTimeOffset at) =>
        (ServiceId is null || ServiceId == service) && (MetricName is null || MetricName == metric) && at >= From && at < Until;
}
