using Sentinel.Contracts.Incidents;
using Sentinel.Contracts.Investigations;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Runtime.Domain.Correlation;
using Sentinel.Runtime.Domain.Incidents;

namespace Sentinel.Runtime.Application.Options;

public sealed class SuppressionOptions
{
    public string? Service { get; set; }

    public string? Metric { get; set; }

    public DateTimeOffset From { get; set; }

    public DateTimeOffset Until { get; set; }

    public string Reason { get; set; } = "maintenance window";
}
