using Sentinel.Contracts.Incidents;
using Sentinel.Contracts.Investigations;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Runtime.Domain.Correlation;
using Sentinel.Runtime.Domain.Incidents;

namespace Sentinel.Runtime.Application.Options;

public sealed class DetectionRuleOptions
{
    public string Service { get; set; } = string.Empty;

    public string Metric { get; set; } = string.Empty;

    public double DetectionThreshold { get; set; }

    public double OnsetThreshold { get; set; } = 1.0;

    public int RequiredSamples { get; set; } = 2;

    public TimeSpan Lookback { get; set; } = TimeSpan.FromMinutes(10);

    public TimeSpan ClearAfter { get; set; } = TimeSpan.FromMinutes(2);
}
