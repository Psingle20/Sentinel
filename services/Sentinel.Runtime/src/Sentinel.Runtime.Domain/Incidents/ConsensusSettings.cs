using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.Runtime.Domain.Incidents;

/// <summary>Configured weights and thresholds for the consensus algorithm (versioned with the decision).</summary>
public sealed record ConsensusSettings
{
    /// <summary>
    /// The version new decisions are made by. v2 no longer counts a mechanism of a leading change, such as Redis
    /// saturation caused by the changed service, as a competing explanation. v3 also lets a standing change claim the
    /// Redis saturation investigators blamed on its own service, so the saturation's evidence counts for the change even
    /// when the saturation scored higher alone. v4 lets it claim the regression of its own service in the same way, so a
    /// faulty dependency deployment is not held back by the regression it caused. Each decision replays by the version it
    /// was made with.
    /// </summary>
    public const string AlgorithmVersion = AlgorithmV4;

    public const string AlgorithmV1 = "consensus-v1";

    public const string AlgorithmV2 = "consensus-v2";

    public const string AlgorithmV3 = "consensus-v3";

    public const string AlgorithmV4 = "consensus-v4";

    /// <summary>Added to the denominator of confidence, so a thinly supported leader is not certain even unopposed.</summary>
    public const double Smoothing = 0.5;

    /// <summary>Reliability of evidence by the prefix of the observations it cites, for example a provider name; unknown sources get <see cref="DefaultReliability"/>.</summary>
    public IReadOnlyDictionary<string, double> SourceReliability { get; init; } = new Dictionary<string, double>(StringComparer.Ordinal);

    public double DefaultReliability { get; init; } = 0.8;

    /// <summary>Weight multiplier for evidence derived from partial data (ADR-007).</summary>
    public double PartialWeight { get; init; } = 0.6;

    /// <summary>Contradicting evidence counts this much more than supporting evidence of the same strength.</summary>
    public double ContradictionWeight { get; init; } = 1.2;

    /// <summary>Below this confidence Sentinel abstains and asks a human to investigate.</summary>
    public double AbstainBelowConfidence { get; init; } = 0.5;

    /// <summary>The leader needs at least this many independent supporting evidence families.</summary>
    public int MinimumIndependentSupport { get; init; } = 2;

    /// <summary>A runner-up within this score margin is recorded as dissent and lowers confidence.</summary>
    public double DissentMargin { get; init; } = 0.25;
}
