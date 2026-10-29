using System.Text.Json.Serialization;
using Sentinel.Contracts.Messaging;
using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.Contracts.Investigations;

/// <summary>
/// One adversarial test of a hypothesis (Phase 05): what was asked, what the evidence said and the
/// evidence in the same report that decided it.
/// </summary>
public sealed record ChallengeFinding
{
    public required HypothesisId HypothesisId { get; init; }

    /// <summary>The falsification question, for example <c>change-serving-before-onset</c>.</summary>
    public required string Falsifier { get; init; }

    public required ChallengeVerdict Verdict { get; init; }

    public required string Detail { get; init; }

    public IReadOnlyList<EvidenceId> EvidenceIds { get; init; } = [];
}
