using Sentinel.Contracts.Incidents;
using Sentinel.Contracts.Investigations;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Runtime.Domain.Correlation;
using Sentinel.Runtime.Domain.Incidents;

namespace Sentinel.Runtime.Application.Options;

/// <summary>Correlation rules (Phase 03), configured as <c>Runtime:Correlation</c>.</summary>
public sealed class CorrelationOptions
{
    public TimeSpan MergeWindow { get; set; } = TimeSpan.FromMinutes(10);

    public TimeSpan ReopenWindow { get; set; } = TimeSpan.FromMinutes(10);

    public int MaxMergeDistance { get; set; } = 1;

    public double MinimumChangeScore { get; set; } = 0.2;

    public TimeSpan ProximityScale { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>How long an observed dependency edge counts toward topology.</summary>
    public TimeSpan EdgeRetention { get; set; } = TimeSpan.FromHours(1);

    public IList<SuppressionOptions> Suppressions { get; } = [];
}
