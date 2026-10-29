using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.Runtime.Domain.Incidents;

/// <summary>
/// The walking skeleton's challenge step (Phase 01A): one deterministic falsification check on
/// the leading hypothesis. Its cause must precede symptom onset in event time, and its support
/// must cite at least two distinct observations; otherwise Runtime abstains. Phase 05 replaces
/// this with adversarial verification and consensus.
/// </summary>
public static class FalsificationCheck
{
    public const string Precedence = "cause-precedes-symptom-onset";
    public const string Corroboration = "support-cites-two-observations";
    public const int MinimumDistinctObservations = 2;

    public static Decision Decide(Incident incident, DecisionId decisionId, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(incident);

        IReadOnlyList<Evidence> evidence = incident.AcceptedEvidence();
        IReadOnlyList<Hypothesis> hypotheses = incident.AllHypotheses();
        int totalEvidence = incident.Reports.Sum(report => report.Report.Evidence.Count);
        double completeness = totalEvidence == 0 ? 0 : (double)evidence.Count / totalEvidence;

        if (hypotheses.Count == 0)
        {
            return Abstain(decisionId, [], completeness, "No hypothesis was proposed.", [], now);
        }

        Hypothesis leader = hypotheses
            .Select((hypothesis, order) => (Hypothesis: hypothesis, Score: Score(hypothesis, evidence), Order: order))
            .OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.Order)
            .First().Hypothesis;
        IReadOnlyList<Evidence> support = Supporting(leader, evidence);

        CheckResult precedence = CheckPrecedence(incident, leader, support);
        int distinct = support.SelectMany(item => item.SourceObservationIds).Distinct().Count();
        var corroboration = new CheckResult(
            Corroboration,
            distinct >= MinimumDistinctObservations,
            $"Supporting evidence cites {distinct} distinct observation(s); {MinimumDistinctObservations} are required.");

        CheckResult[] checks = [precedence, corroboration];
        HypothesisId[] others = [.. hypotheses.Where(hypothesis => hypothesis.HypothesisId != leader.HypothesisId).Select(hypothesis => hypothesis.HypothesisId)];

        if (checks.All(check => check.Passed))
        {
            double confidence = Math.Clamp(support.Average(item => item.Strength), 0, 0.95);
            return new Decision
            {
                DecisionId = decisionId,
                Result = DecisionResult.Diagnosed,
                LeadingHypothesisId = leader.HypothesisId,
                RejectedHypothesisIds = others,
                Confidence = confidence,
                EvidenceCompleteness = completeness,
                Rationale = $"{leader.Statement} It precedes symptom onset and is supported by {distinct} distinct observations.",
                Checks = checks,
                DecidedAt = now
            };
        }

        string failed = string.Join("; ", checks.Where(check => !check.Passed).Select(check => check.Detail));
        return Abstain(decisionId, [.. hypotheses.Select(hypothesis => hypothesis.HypothesisId)], completeness, $"The leading hypothesis failed falsification: {failed}", checks, now);
    }

    private static Decision Abstain(DecisionId decisionId, IReadOnlyList<HypothesisId> unresolved, double completeness, string rationale, IReadOnlyList<CheckResult> checks, DateTimeOffset now) => new()
    {
        DecisionId = decisionId,
        Result = DecisionResult.Abstained,
        RejectedHypothesisIds = [],
        Confidence = 0,
        EvidenceCompleteness = completeness,
        Rationale = unresolved.Count == 0 ? rationale : $"{rationale} {unresolved.Count} hypothesis(es) remain unresolved.",
        Checks = checks,
        DecidedAt = now
    };

    private static double Score(Hypothesis hypothesis, IReadOnlyList<Evidence> evidence) =>
        Supporting(hypothesis, evidence).Sum(item => item.Strength)
        - evidence.Where(item => item.Direction == EvidenceDirection.Contradicts
                && (item.HypothesisIds.Contains(hypothesis.HypothesisId) || hypothesis.ContradictingEvidenceIds.Contains(item.EvidenceId)))
            .Sum(item => item.Strength);

    private static List<Evidence> Supporting(Hypothesis hypothesis, IReadOnlyList<Evidence> evidence) =>
        [.. evidence.Where(item => item.Direction == EvidenceDirection.Supports
            && (item.HypothesisIds.Contains(hypothesis.HypothesisId) || hypothesis.SupportingEvidenceIds.Contains(item.EvidenceId)))];

    private static CheckResult CheckPrecedence(Incident incident, Hypothesis leader, IReadOnlyList<Evidence> support)
    {
        DateTimeOffset onset = incident.SymptomOnset;
        if (leader.SuspectedChangeObservationId is { } changeId)
        {
            LinkedChange? change = incident.Changes.FirstOrDefault(candidate => candidate.ObservationIds.Contains(changeId));
            if (change is null)
            {
                return new CheckResult(Precedence, false, $"The suspected change {changeId} is not among the correlated changes.");
            }

            // A deployment can cause symptoms only once its new version serves.
            if (change.CompletedAt is not { } serving)
            {
                return new CheckResult(Precedence, false, $"{change.ServiceId} {change.Version} was still rolling out, so it was not serving when symptoms began at {onset:O}.");
            }

            return serving < onset
                ? new CheckResult(Precedence, true, $"{change.ServiceId} {change.Version} served from {serving:O}, before symptom onset at {onset:O}.")
                : new CheckResult(Precedence, false, $"{change.ServiceId} {change.Version} served only from {serving:O}, after symptoms began at {onset:O}.");
        }

        if (support.Count == 0)
        {
            return new CheckResult(Precedence, false, "No supporting evidence establishes when the cause began.");
        }

        DateTimeOffset earliest = support.Min(item => item.From);
        return earliest < onset
            ? new CheckResult(Precedence, true, $"The earliest supporting signal began at {earliest:O}, before symptom onset at {onset:O}.")
            : new CheckResult(Precedence, false, $"The earliest supporting signal began at {earliest:O}, not before symptom onset at {onset:O}.");
    }
}
