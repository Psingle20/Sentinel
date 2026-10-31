using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.Contracts.Investigations;

/// <summary>How one surviving explanation stood in an ambiguous consensus: its score and its independent support and contradiction.</summary>
public sealed record HypothesisStanding(HypothesisId HypothesisId, double Score, int IndependentSupport, int IndependentContradiction);
