using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Runtime.Domain.Incidents.Events;

namespace Sentinel.Runtime.Domain.Incidents;

/// <summary>
/// The evidence blackboard (Phase 04). Specialists investigate in parallel and propose hypotheses
/// independently; proposals with the same key are one explanation and merge, keeping their lineage.
/// A key that extends another (<c>redis-saturation:reporting-worker</c> extends <c>redis-saturation</c>)
/// refines it: the broader explanation stops competing and its evidence counts for each refinement,
/// in the refinement's own independence groups. Deterministic: the same reports give the same board.
/// </summary>
public sealed class HypothesisBlackboard
{
    private readonly Dictionary<HypothesisId, HypothesisId> _canonical = [];
    private readonly Dictionary<HypothesisId, IReadOnlyList<HypothesisId>> _expansion = [];

    private HypothesisBlackboard(IReadOnlyList<BlackboardHypothesis> all) => All = all;

    public static HypothesisBlackboard Empty { get; } = new([]);

    public IReadOnlyList<BlackboardHypothesis> All { get; }

    /// <summary>The explanations that compete for the decision: merged, and not subsumed by a refinement.</summary>
    public IReadOnlyList<Hypothesis> Competing => [.. All.Where(entry => !entry.Subsumed).Select(entry => entry.Hypothesis)];

    public static string? ParentKey(string? key)
    {
        int separator = key?.IndexOf(':', StringComparison.Ordinal) ?? -1;
        return separator > 0 && !key!.StartsWith("change:", StringComparison.Ordinal) ? key[..separator] : null;
    }

    public static HypothesisBlackboard Build(IReadOnlyList<InvestigationReported> reports)
    {
        ArgumentNullException.ThrowIfNull(reports);

        var groups = new List<(string Identity, List<(Hypothesis Hypothesis, InvestigationId Investigation)> Proposals)>();
        foreach (InvestigationReported reported in reports)
        {
            foreach (Hypothesis hypothesis in reported.Report.Hypotheses)
            {
                string identity = hypothesis.Key ?? $"id:{hypothesis.HypothesisId}";
                int index = groups.FindIndex(group => group.Identity == identity);
                if (index < 0)
                {
                    groups.Add((identity, [(hypothesis, reported.Report.InvestigationId)]));
                }
                else
                {
                    groups[index].Proposals.Add((hypothesis, reported.Report.InvestigationId));
                }
            }
        }

        var merged = groups.Select(group => Merge(group.Proposals)).ToList();
        var byKey = merged.Where(hypothesis => hypothesis.Key is not null).ToDictionary(hypothesis => hypothesis.Key!, StringComparer.Ordinal);
        var children = merged
            .Where(hypothesis => ParentKey(hypothesis.Key) is { } parent && byKey.ContainsKey(parent))
            .GroupBy(hypothesis => ParentKey(hypothesis.Key)!, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.Ordinal);

        var entries = new List<BlackboardHypothesis>();
        for (int index = 0; index < merged.Count; index++)
        {
            Hypothesis hypothesis = merged[index];
            bool subsumed = hypothesis.Key is { } key && children.ContainsKey(key);
            Hypothesis? parent = ParentKey(hypothesis.Key) is { } parentKey && byKey.TryGetValue(parentKey, out Hypothesis? found) ? found : null;
            if (parent is not null)
            {
                // A refinement inherits the broader explanation's links, so the evidence that Redis is saturated counts for each culprit.
                hypothesis = hypothesis with
                {
                    SupportingEvidenceIds = [.. hypothesis.SupportingEvidenceIds.Union(parent.SupportingEvidenceIds)],
                    ContradictingEvidenceIds = [.. hypothesis.ContradictingEvidenceIds.Union(parent.ContradictingEvidenceIds)]
                };
            }

            entries.Add(new BlackboardHypothesis(
                hypothesis,
                [.. groups[index].Proposals.Select(proposal => proposal.Hypothesis.HypothesisId)],
                [.. groups[index].Proposals.Select(proposal => proposal.Investigation).Distinct()],
                parent?.HypothesisId,
                subsumed));
        }

        var board = new HypothesisBlackboard(entries);
        foreach (BlackboardHypothesis entry in entries)
        {
            foreach (HypothesisId original in entry.Lineage)
            {
                board._canonical[original] = entry.Hypothesis.HypothesisId;
            }

            if (entry.Subsumed)
            {
                board._expansion[entry.Hypothesis.HypothesisId] = [.. children[entry.Hypothesis.Key!].Select(child => child.HypothesisId)];
            }
        }

        return board;
    }

    /// <summary>The blackboard hypothesis an original proposal now belongs to.</summary>
    public HypothesisId Canonical(HypothesisId proposed) => _canonical.TryGetValue(proposed, out HypothesisId canonical) ? canonical : proposed;

    public bool Knows(HypothesisId hypothesisId) => _canonical.ContainsKey(hypothesisId);

    /// <summary>Re-points evidence at the blackboard's hypotheses; evidence for a subsumed explanation points at its refinements.</summary>
    public Evidence Remap(Evidence evidence)
    {
        ArgumentNullException.ThrowIfNull(evidence);

        HypothesisId[] targets = [.. evidence.HypothesisIds
            .Select(Canonical)
            .SelectMany(id => _expansion.TryGetValue(id, out IReadOnlyList<HypothesisId>? refined) ? refined : [id])
            .Distinct()];
        return targets.SequenceEqual(evidence.HypothesisIds) ? evidence : evidence with { HypothesisIds = targets };
    }

    private static Hypothesis Merge(List<(Hypothesis Hypothesis, InvestigationId Investigation)> proposals)
    {
        Hypothesis first = proposals[0].Hypothesis;
        return proposals.Count == 1
            ? first
            : first with
            {
                SuspectedServiceId = proposals.Select(proposal => proposal.Hypothesis.SuspectedServiceId).FirstOrDefault(service => service is not null),
                SuspectedChangeObservationId = proposals.Select(proposal => proposal.Hypothesis.SuspectedChangeObservationId).FirstOrDefault(change => change is not null),
                SupportingEvidenceIds = [.. proposals.SelectMany(proposal => proposal.Hypothesis.SupportingEvidenceIds).Distinct()],
                ContradictingEvidenceIds = [.. proposals.SelectMany(proposal => proposal.Hypothesis.ContradictingEvidenceIds).Distinct()]
            };
    }
}
