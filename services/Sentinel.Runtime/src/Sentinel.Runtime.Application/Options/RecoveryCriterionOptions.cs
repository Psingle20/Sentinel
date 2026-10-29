using Sentinel.Contracts.Incidents;
using Sentinel.Contracts.Investigations;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Runtime.Domain.Correlation;
using Sentinel.Runtime.Domain.Incidents;

namespace Sentinel.Runtime.Application.Options;

public sealed class RecoveryCriterionOptions
{
    public string? Service { get; set; }

    public string Metric { get; set; } = string.Empty;

    public ThresholdComparison Comparison { get; set; } = ThresholdComparison.LessThan;

    public double Threshold { get; set; }
}
