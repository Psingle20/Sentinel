using Sentinel.Contracts.Investigations;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Contracts.Remediation;

namespace Sentinel.Contracts.Incidents;

/// <summary>One adversarial challenge of a hypothesis and how it ended (Phase 05).</summary>
public sealed record ChallengeView(HypothesisId HypothesisId, string Falsifier, ChallengeVerdict Verdict, string Detail, IReadOnlyList<EvidenceId> EvidenceIds, InvestigationId InvestigationId);
