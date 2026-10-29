using Sentinel.Contracts.Investigations;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Contracts.Remediation;

namespace Sentinel.Contracts.Incidents;

public sealed record DecisionView
{
    public required DecisionId DecisionId { get; init; }

    public required DecisionOutcome Outcome { get; init; }

    public HypothesisId? LeadingHypothesisId { get; init; }

    public IReadOnlyList<HypothesisId> RejectedHypothesisIds { get; init; } = [];

    public required double Confidence { get; init; }

    public required double EvidenceCompleteness { get; init; }

    public required Calibration Calibration { get; init; }

    public required string Rationale { get; init; }

    public IReadOnlyList<DecisionCheck> Checks { get; init; } = [];

    public required DateTimeOffset DecidedAt { get; init; }

    /// <summary>Alternatives within the dissent margin of the leader: disagreement kept, not discarded (Phase 05).</summary>
    public IReadOnlyList<HypothesisId> DissentingHypothesisIds { get; init; } = [];

    /// <summary>Why each hypothesis won or lost: its score, independent support and contradiction.</summary>
    public IReadOnlyList<HypothesisAssessmentView> Assessments { get; init; } = [];

    /// <summary>What the investigators could not determine.</summary>
    public IReadOnlyList<string> MissingEvidence { get; init; } = [];

    public string? AlgorithmVersion { get; init; }

    /// <summary>Hash of the versioned inputs, so a replay can show it decided on the same evidence.</summary>
    public string? InputsHash { get; init; }

    /// <summary>Set when a later decision superseded this one.</summary>
    public DecisionId? SupersededBy { get; init; }
}
