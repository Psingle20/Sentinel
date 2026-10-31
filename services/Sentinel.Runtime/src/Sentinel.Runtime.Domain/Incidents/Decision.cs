using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.Runtime.Domain.Incidents;

public sealed record Decision
{
    public required DecisionId DecisionId { get; init; }

    public required DecisionResult Result { get; init; }

    public HypothesisId? LeadingHypothesisId { get; init; }

    public IReadOnlyList<HypothesisId> RejectedHypothesisIds { get; init; } = [];

    public required double Confidence { get; init; }

    public required double EvidenceCompleteness { get; init; }

    public required string Rationale { get; init; }

    public IReadOnlyList<CheckResult> Checks { get; init; } = [];

    /// <summary>Alternatives that stand within the dissent margin of the leader: disagreement preserved, not discarded.</summary>
    public IReadOnlyList<HypothesisId> DissentingHypothesisIds { get; init; } = [];

    public IReadOnlyList<HypothesisAssessment> Assessments { get; init; } = [];

    public string AlgorithmVersion { get; init; } = "falsification-v1";

    /// <summary>Why consensus abstained, when it did (Phase E05); null for a diagnosis, the falsification check and decisions recorded before.</summary>
    public AbstentionReason? AbstainedFor { get; init; }

    /// <summary>For an abstention for ambiguity: the leader and its closest rival, the two explanations one observation should separate.</summary>
    public IReadOnlyList<HypothesisId> Contenders { get; init; } = [];

    /// <summary>What the investigators could not determine and the decision lacked.</summary>
    public IReadOnlyList<string> MissingEvidence { get; init; } = [];

    /// <summary>Hash of the versioned inputs, so a replay can show it decided on the same evidence.</summary>
    public string? InputsHash { get; init; }

    /// <summary>Set when a later decision supersedes this one; the history is kept.</summary>
    public DecisionId? SupersededBy { get; init; }

    public required DateTimeOffset DecidedAt { get; init; }

    /// <summary>The consensus settings the decision was made with, so a domain replay re-runs it exactly (Phase 07); null for falsification-only decisions and decisions recorded before Phase 07.</summary>
    public ConsensusSettings? Settings { get; init; }
}
