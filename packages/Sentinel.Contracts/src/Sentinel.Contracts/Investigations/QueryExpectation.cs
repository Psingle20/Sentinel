using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.Contracts.Investigations;

/// <summary>
/// What one hypothesis predicts a discriminating query will show (Phase E05): the named service's metric, read at symptom
/// onset, compared with a threshold. A prediction that the observation meets supports the hypothesis; one it misses
/// contradicts it.
/// </summary>
public sealed record QueryExpectation
{
    public required HypothesisId HypothesisId { get; init; }

    public required ServiceId ServiceId { get; init; }

    public required string Metric { get; init; }

    /// <summary><c>above</c> or <c>below</c>.</summary>
    public required string Comparison { get; init; }

    public required double Threshold { get; init; }

    /// <summary>The prediction in one sentence, for people reading the incident.</summary>
    public required string Statement { get; init; }
}
