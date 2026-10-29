using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.Runtime.Domain.Incidents;

/// <summary>
/// Reliability-weighted consensus with falsification (Phase 05). Copies of one signal count once
/// (independence groups), contradicting evidence lowers a hypothesis, a falsified hypothesis cannot
/// lead, and confidence is reported with evidence completeness. The same inputs always give the same
/// decision; <see cref="Decision.InputsHash"/> identifies them for replay.
/// </summary>
public static class Consensus
{
    private const string ChangeKey = "change:";
    private const string RedisSaturationKey = "redis-saturation";

    private static readonly string[] Versions = [ConsensusSettings.AlgorithmV1, ConsensusSettings.AlgorithmV2, ConsensusSettings.AlgorithmV3];

    /// <summary>Whether <paramref name="algorithm"/> names a consensus version this code can decide and replay by.</summary>
    public static bool IsKnown(string? algorithm) => algorithm is not null && Versions.Contains(algorithm, StringComparer.Ordinal);

    public static Decision Decide(Incident incident, IReadOnlyList<Challenge> challenges, ConsensusSettings settings, DecisionId decisionId, DateTimeOffset now) =>
        Decide(incident, challenges, settings, decisionId, now, ConsensusSettings.AlgorithmVersion);

    /// <summary>Decides by <paramref name="algorithm"/>, so a replay applies the rules the recorded decision was made by.</summary>
    public static Decision Decide(Incident incident, IReadOnlyList<Challenge> challenges, ConsensusSettings settings, DecisionId decisionId, DateTimeOffset now, string algorithm)
    {
        ArgumentNullException.ThrowIfNull(incident);
        ArgumentNullException.ThrowIfNull(challenges);
        ArgumentNullException.ThrowIfNull(settings);
        if (!IsKnown(algorithm))
        {
            throw new ArgumentException($"Unknown consensus algorithm '{algorithm}'.", nameof(algorithm));
        }

        IReadOnlyList<Evidence> evidence = incident.AcceptedEvidence();
        IReadOnlyList<Hypothesis> hypotheses = incident.AllHypotheses();
        int totalEvidence = incident.Reports.Sum(report => report.Report.Evidence.Count);
        double completeness = Completeness(evidence, totalEvidence, settings);
        string inputsHash = InputsHash(hypotheses, evidence, challenges, settings, algorithm);

        HypothesisAssessment[] alone = [.. hypotheses.Select(hypothesis => Assess(incident, hypothesis, evidence, challenges, settings, []))];

        // v3: a standing change claims the Redis saturation investigators blamed on its own service. The saturation is how
        // the change caused the symptoms, so its evidence counts for the change and it does not compete on its own.
        Dictionary<HypothesisId, Hypothesis> claimed = algorithm == ConsensusSettings.AlgorithmV3 ? Claimed(hypotheses, alone) : [];
        HypothesisAssessment[] assessments = claimed.Count == 0
            ? alone
            : [.. hypotheses.Select(hypothesis => Assess(incident, hypothesis, evidence, challenges, settings, [.. hypotheses.Where(other => claimed.TryGetValue(other.HypothesisId, out Hypothesis? change) && change.HypothesisId == hypothesis.HypothesisId)]))];
        HypothesisAssessment[] standing = [.. assessments.Where(assessment => !assessment.Falsified && !claimed.ContainsKey(assessment.HypothesisId)).OrderByDescending(assessment => assessment.Score).ThenBy(assessment => assessment.HypothesisId.ToString(), StringComparer.Ordinal)];
        HypothesisId[] falsified = [.. assessments.Where(assessment => assessment.Falsified).Select(assessment => assessment.HypothesisId)];

        var checks = new List<CheckResult>();
        if (standing.Length == 0)
        {
            checks.Add(new CheckResult("hypothesis-survives-challenge", false, "Every hypothesis was falsified or none was proposed."));
            return Abstain(decisionId, falsified, completeness, "No explanation survived challenge.", checks, inputsHash, now, algorithm) with { Assessments = assessments };
        }

        HypothesisAssessment leader = standing[0];
        Hypothesis leading = hypotheses.First(hypothesis => hypothesis.HypothesisId == leader.HypothesisId);

        // v2: an explanation of how the leading change caused the symptoms agrees with it, so it is neither the runner-up nor dissent.
        HypothesisId[] mechanisms = algorithm == ConsensusSettings.AlgorithmV1
            ? []
            : [.. standing.Skip(1).Where(other => IsMechanismOf(hypotheses.First(hypothesis => hypothesis.HypothesisId == other.HypothesisId), leading)).Select(other => other.HypothesisId), .. claimed.Keys];
        HypothesisAssessment[] competitors = [.. standing.Skip(1).Where(other => !mechanisms.Contains(other.HypothesisId))];
        HypothesisAssessment? runnerUp = competitors.FirstOrDefault();

        CheckResult precedence = Precedence(incident, leading, evidence);
        checks.Add(precedence);
        checks.Add(new CheckResult(
            "independent-corroboration",
            leader.IndependentSupport >= settings.MinimumIndependentSupport,
            string.Create(CultureInfo.InvariantCulture, $"{leader.IndependentSupport} independent evidence families support the leader; {settings.MinimumIndependentSupport} are required.")));

        double margin = runnerUp is null ? leader.Score : leader.Score - runnerUp.Score;
        double confidence = Math.Clamp(leader.Score / (leader.Score + Math.Max(runnerUp?.Score ?? 0, 0) + ConsensusSettings.Smoothing), 0, 0.95) * completeness;
        HypothesisId[] dissent = [.. competitors.Where(other => leader.Score - other.Score < settings.DissentMargin).Select(other => other.HypothesisId)];
        checks.Add(new CheckResult(
            "confidence-above-threshold",
            confidence >= settings.AbstainBelowConfidence,
            string.Create(CultureInfo.InvariantCulture, $"Confidence {confidence:F2} with evidence completeness {completeness:F2}; Sentinel abstains below {settings.AbstainBelowConfidence:F2}. Margin over the runner-up {margin:F2}.")));

        if (!checks.TrueForAll(check => check.Passed) || leader.Score <= 0)
        {
            string failed = string.Join("; ", checks.Where(check => !check.Passed).Select(check => check.Detail));
            return Abstain(decisionId, falsified, completeness, $"The leading explanation is not established: {failed}", checks, inputsHash, now, algorithm) with { DissentingHypothesisIds = dissent, Assessments = assessments, Confidence = Math.Round(confidence, 4) };
        }

        return new Decision
        {
            DecisionId = decisionId,
            Result = DecisionResult.Diagnosed,
            LeadingHypothesisId = leader.HypothesisId,
            RejectedHypothesisIds = [.. assessments.Where(assessment => assessment.HypothesisId != leader.HypothesisId && !dissent.Contains(assessment.HypothesisId) && !mechanisms.Contains(assessment.HypothesisId)).Select(assessment => assessment.HypothesisId)],
            DissentingHypothesisIds = dissent,
            Confidence = confidence,
            EvidenceCompleteness = completeness,
            Rationale = $"{leading.Statement} It scores {leader.Score:F2} from {leader.IndependentSupport} independent evidence families against {runnerUp?.Score ?? 0:F2} for the next explanation, and it survived {challenges.Count(challenge => challenge.HypothesisId == leader.HypothesisId && challenge.Verdict == ChallengeVerdict.Survived)} challenge(s).",
            Checks = checks,
            Assessments = assessments,
            AlgorithmVersion = algorithm,
            InputsHash = inputsHash,
            DecidedAt = now
        };
    }

    /// <summary>
    /// Redis saturation with no culprit, or caused by the leading change's own service, is how that change produced the
    /// symptoms (the key vocabulary's <c>redis-saturation[:culprit]</c>), not another cause.
    /// </summary>
    private static bool IsMechanismOf(Hypothesis candidate, Hypothesis leading) =>
        leading.Key is { } cause && cause.StartsWith(ChangeKey, StringComparison.Ordinal)
        && candidate.Key is { } mechanism
        && (mechanism == RedisSaturationKey
            || leading.SuspectedServiceId is { } service && mechanism == $"{RedisSaturationKey}:{service}");

    /// <summary>
    /// The saturation hypotheses each standing change claims: Redis saturation blamed on exactly the change's service
    /// (<c>redis-saturation:&lt;service&gt;</c>), when one standing change, and only one, names that service. Saturation
    /// with no culprit, or blamed on another service, is not claimed: it may be a different cause.
    /// </summary>
    private static Dictionary<HypothesisId, Hypothesis> Claimed(IReadOnlyList<Hypothesis> hypotheses, IReadOnlyList<HypothesisAssessment> assessments)
    {
        bool Stands(Hypothesis hypothesis) => !assessments.First(assessment => assessment.HypothesisId == hypothesis.HypothesisId).Falsified;
        Hypothesis[] changes = [.. hypotheses.Where(hypothesis => hypothesis.Key is { } key && key.StartsWith(ChangeKey, StringComparison.Ordinal) && hypothesis.SuspectedServiceId is not null && Stands(hypothesis))];
        var claimed = new Dictionary<HypothesisId, Hypothesis>();
        foreach (Hypothesis saturation in hypotheses.Where(hypothesis => hypothesis.Key is { } key && key.StartsWith($"{RedisSaturationKey}:", StringComparison.Ordinal) && Stands(hypothesis)))
        {
            Hypothesis[] owners = [.. changes.Where(change => saturation.Key == $"{RedisSaturationKey}:{change.SuspectedServiceId}")];
            if (owners.Length == 1)
            {
                claimed[saturation.HypothesisId] = owners[0];
            }
        }

        return claimed;
    }

    private static HypothesisAssessment Assess(Incident incident, Hypothesis hypothesis, IReadOnlyList<Evidence> evidence, IReadOnlyList<Challenge> challenges, ConsensusSettings settings, IReadOnlyList<Hypothesis> claims)
    {
        // A claimed mechanism's supporting evidence supports the change that claims it; families still count once.
        Evidence[] supporting = [.. evidence.Where(item => item.Direction == EvidenceDirection.Supports && (Links(item, hypothesis, hypothesis.SupportingEvidenceIds) || claims.Any(claim => Links(item, claim, claim.SupportingEvidenceIds))))];
        Evidence[] contradicting = [.. evidence.Where(item => item.Direction == EvidenceDirection.Contradicts && Links(item, hypothesis, hypothesis.ContradictingEvidenceIds))];

        // Each independence group counts once, at its strongest weighted value.
        double support = Families(supporting).Sum(family => family.Max(item => Weight(item, settings)));
        double contradiction = Families(contradicting).Sum(family => family.Max(item => Weight(item, settings))) * settings.ContradictionWeight;

        Challenge[] own = [.. challenges.Where(challenge => challenge.HypothesisId == hypothesis.HypothesisId)];
        bool falsified = own.Any(challenge => challenge.Verdict == ChallengeVerdict.Falsified);
        double survivedBonus = own.Count(challenge => challenge.Verdict == ChallengeVerdict.Survived) * 0.1;

        CheckResult precedence = Precedence(incident, hypothesis, evidence);
        bool precedes = precedence.Passed;
        string stands = claims.Count > 0 ? $"Stands, and explains {string.Join(", ", claims.Select(claim => claim.Key))}." : "Stands.";
        string reason = precedes ? stands : $"Fails precedence: {precedence.Detail}";
        if (falsified)
        {
            reason = $"Falsified: {own.First(challenge => challenge.Verdict == ChallengeVerdict.Falsified).Detail}";
        }

        return new HypothesisAssessment(
            hypothesis.HypothesisId,
            Math.Round(support - contradiction + survivedBonus, 4),
            Families(supporting).Count(),
            Families(contradicting).Count(),
            falsified || !precedes,
            reason);
    }

    private static bool Links(Evidence item, Hypothesis hypothesis, IReadOnlyList<EvidenceId> listed) =>
        item.HypothesisIds.Contains(hypothesis.HypothesisId) || listed.Contains(item.EvidenceId);

    /// <summary>
    /// Evidence families: items join one family when they declare the same independence group or cite
    /// any observation in common, so a copy of a signal counts once however it is labelled.
    /// </summary>
    private static IEnumerable<IGrouping<string, Evidence>> Families(IEnumerable<Evidence> items)
    {
        Evidence[] evidence = [.. items];
        int[] parent = [.. Enumerable.Range(0, evidence.Length)];
        int Find(int index)
        {
            while (parent[index] != index)
            {
                index = parent[index];
            }

            return index;
        }

        for (int left = 0; left < evidence.Length; left++)
        {
            for (int right = left + 1; right < evidence.Length; right++)
            {
                bool sameGroup = evidence[left].IndependenceGroup is { } group && group == evidence[right].IndependenceGroup;
                if (sameGroup || evidence[left].SourceObservationIds.Intersect(evidence[right].SourceObservationIds).Any())
                {
                    parent[Find(right)] = Find(left);
                }
            }
        }

        return evidence.Select((item, index) => (Item: item, Family: Find(index)))
            .GroupBy(entry => evidence[entry.Family].EvidenceId.ToString(), entry => entry.Item, StringComparer.Ordinal);
    }

    private static double Weight(Evidence item, ConsensusSettings settings)
    {
        double reliability = item.SourceObservationIds
            .Select(id => settings.SourceReliability.TryGetValue(id.Value.Split(':')[0], out double value) ? value : settings.DefaultReliability)
            .DefaultIfEmpty(settings.DefaultReliability)
            .Max();
        return item.Strength * reliability * (item.Partial ? settings.PartialWeight : 1);
    }

    /// <summary>Accepted evidence over all evidence, discounted for evidence built on partial data.</summary>
    private static double Completeness(IReadOnlyList<Evidence> accepted, int total, ConsensusSettings settings)
    {
        if (total == 0)
        {
            return 0;
        }

        double weighted = accepted.Sum(item => item.Partial ? settings.PartialWeight : 1);
        return Math.Round(weighted / total, 4);
    }

    private static CheckResult Precedence(Incident incident, Hypothesis hypothesis, IReadOnlyList<Evidence> evidence)
    {
        DateTimeOffset onset = incident.SymptomOnset;
        if (hypothesis.SuspectedChangeObservationId is { } changeId)
        {
            LinkedChange? change = incident.Changes.FirstOrDefault(candidate => candidate.ObservationIds.Contains(changeId));
            if (change is null)
            {
                return new CheckResult(FalsificationCheck.Precedence, false, $"The suspected change {changeId} is not among the correlated changes.");
            }

            if (change.CompletedAt is not { } serving)
            {
                return new CheckResult(FalsificationCheck.Precedence, false, $"{change.ServiceId} {change.Version} was still rolling out when symptoms began at {onset:O}.");
            }

            return serving < onset
                ? new CheckResult(FalsificationCheck.Precedence, true, $"{change.ServiceId} {change.Version} served from {serving:O}, before symptom onset at {onset:O}.")
                : new CheckResult(FalsificationCheck.Precedence, false, $"{change.ServiceId} {change.Version} served only from {serving:O}, after symptoms began at {onset:O}.");
        }

        Evidence[] support = [.. evidence.Where(item => item.Direction == EvidenceDirection.Supports && Links(item, hypothesis, hypothesis.SupportingEvidenceIds))];
        if (support.Length == 0)
        {
            return new CheckResult(FalsificationCheck.Precedence, false, "No supporting evidence establishes when the cause began.");
        }

        DateTimeOffset earliest = support.Min(item => item.From);
        return earliest < onset
            ? new CheckResult(FalsificationCheck.Precedence, true, $"The earliest supporting signal began at {earliest:O}, before symptom onset at {onset:O}.")
            : new CheckResult(FalsificationCheck.Precedence, false, $"The earliest supporting signal began at {earliest:O}, not before symptom onset at {onset:O}.");
    }

    private static Decision Abstain(DecisionId decisionId, IReadOnlyList<HypothesisId> rejected, double completeness, string rationale, IReadOnlyList<CheckResult> checks, string inputsHash, DateTimeOffset now, string algorithm) => new()
    {
        DecisionId = decisionId,
        Result = DecisionResult.Abstained,
        RejectedHypothesisIds = rejected,
        Confidence = 0,
        EvidenceCompleteness = completeness,
        Rationale = rationale,
        Checks = checks,
        AlgorithmVersion = algorithm,
        InputsHash = inputsHash,
        DecidedAt = now
    };

    /// <summary>Identifies the decision's inputs, so a replay can show it saw exactly the same evidence.</summary>
    private static string InputsHash(IReadOnlyList<Hypothesis> hypotheses, IReadOnlyList<Evidence> evidence, IReadOnlyList<Challenge> challenges, ConsensusSettings settings, string algorithm)
    {
        var text = new StringBuilder(algorithm);
        foreach (Hypothesis hypothesis in hypotheses.OrderBy(item => item.HypothesisId.ToString(), StringComparer.Ordinal))
        {
            text.Append('|').Append(hypothesis.HypothesisId).Append(':').Append(hypothesis.SuspectedChangeObservationId);
        }

        foreach (Evidence item in evidence.OrderBy(item => item.EvidenceId.ToString(), StringComparer.Ordinal))
        {
            text.Append('|').Append(item.EvidenceId).Append(':').Append(item.Direction).Append(':').Append(item.Strength.ToString("R", CultureInfo.InvariantCulture)).Append(':').Append(item.Partial);
        }

        foreach (Challenge challenge in challenges.OrderBy(item => item.HypothesisId.ToString(), StringComparer.Ordinal).ThenBy(item => item.Falsifier, StringComparer.Ordinal))
        {
            text.Append('|').Append(challenge.HypothesisId).Append(':').Append(challenge.Verdict);
        }

        text.Append('|').Append(settings.AbstainBelowConfidence.ToString("R", CultureInfo.InvariantCulture));
        return "sha256:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())));
    }
}
