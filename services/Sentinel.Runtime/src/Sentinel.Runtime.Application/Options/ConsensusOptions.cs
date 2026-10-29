using Sentinel.Contracts.Incidents;
using Sentinel.Contracts.Investigations;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Runtime.Domain.Correlation;
using Sentinel.Runtime.Domain.Incidents;

namespace Sentinel.Runtime.Application.Options;

/// <summary>Reliability-weighted consensus (Phase 05); off until the verifier runs, when the walking skeleton's falsification check decides.</summary>
public sealed class ConsensusOptions
{
    public bool Enabled { get; set; }

    public IDictionary<string, double> SourceReliability { get; } = new Dictionary<string, double>(StringComparer.Ordinal);

    public double DefaultReliability { get; set; } = 0.8;

    public double PartialWeight { get; set; } = 0.6;

    public double ContradictionWeight { get; set; } = 1.2;

    public double AbstainBelowConfidence { get; set; } = 0.5;

    public int MinimumIndependentSupport { get; set; } = 2;

    public double DissentMargin { get; set; } = 0.25;
}
