using System.Globalization;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Runtime.Domain.Incidents;

namespace Sentinel.Runtime.Domain.Correlation;

/// <summary>Tunable correlation rules (Phase 03), configured as <c>Runtime:Correlation</c>.</summary>
public sealed record CorrelationSettings
{
    /// <summary>A related service's symptom starting within this long of an open incident's onset is merged into it.</summary>
    public TimeSpan MergeWindow { get; init; } = TimeSpan.FromMinutes(10);

    /// <summary>The same symptom returning within this long of resolution, with no new change, reopens the incident.</summary>
    public TimeSpan ReopenWindow { get; init; } = TimeSpan.FromMinutes(10);

    /// <summary>Services at most this many dependency hops apart are related.</summary>
    public int MaxMergeDistance { get; init; } = 1;

    /// <summary>Changes scoring below this are not linked to a new incident.</summary>
    public double MinimumChangeScore { get; init; } = 0.2;

    /// <summary>Temporal proximity halves roughly every <c>0.7 ×</c> this interval.</summary>
    public TimeSpan ProximityScale { get; init; } = TimeSpan.FromMinutes(5);
}
