using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Sentinel.AgentWorker.Application.Tools;
using Sentinel.AgentWorker.Domain.Executions;
using Sentinel.Contracts.Incidents;
using Sentinel.Contracts.Investigations;
using Sentinel.Contracts.Messaging;
using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.AgentWorker.Application.Investigations;

/// <summary>Compares two findings by meaning: identifiers minted per run are ignored, everything stated is compared.</summary>
public static class FindingsComparison
{
    public static IReadOnlyList<string> Differences(InvestigationCompleted recorded, InvestigationCompleted replayed)
    {
        ArgumentNullException.ThrowIfNull(recorded);
        ArgumentNullException.ThrowIfNull(replayed);

        var differences = new List<string>();
        if (recorded.Outcome != replayed.Outcome)
        {
            differences.Add($"Outcome {recorded.Outcome} was replayed as {replayed.Outcome}.");
        }

        Compare("hypothesis", recorded.Hypotheses.Select(Hypothesis), replayed.Hypotheses.Select(Hypothesis), differences);
        Compare("evidence", recorded.Evidence.Select(Evidence), replayed.Evidence.Select(Evidence), differences);
        Compare("challenge", (recorded.Challenges ?? []).Select(challenge => Challenge(challenge, recorded)), (replayed.Challenges ?? []).Select(challenge => Challenge(challenge, replayed)), differences);
        Compare("unknown", recorded.Unknowns ?? [], replayed.Unknowns ?? [], differences);
        return differences;
    }

    private static string Hypothesis(HypothesisFinding hypothesis) =>
        $"{hypothesis.Key ?? hypothesis.Statement} | {hypothesis.SuspectedServiceId} | {hypothesis.SuspectedChangeObservationId}";

    private static string Evidence(EvidenceFinding evidence) =>
        string.Create(CultureInfo.InvariantCulture, $"{evidence.Statement} | {evidence.Direction} | {evidence.Strength:F3} | {string.Join(",", evidence.SourceObservationIds.Select(id => id.ToString()).Order(StringComparer.Ordinal))} | {evidence.IndependenceGroup}");

    /// <summary>A challenge of the run's own hypothesis is named by that hypothesis, whose ID is minted per run; a challenged input hypothesis keeps its ID.</summary>
    private static string Challenge(ChallengeFinding challenge, InvestigationCompleted findings)
    {
        HypothesisFinding? own = findings.Hypotheses.FirstOrDefault(hypothesis => hypothesis.HypothesisId == challenge.HypothesisId);
        return $"{(own is null ? challenge.HypothesisId.ToString() : Hypothesis(own))} | {challenge.Falsifier} | {challenge.Verdict}";
    }

    private static void Compare(string kind, IEnumerable<string> recorded, IEnumerable<string> replayed, List<string> differences)
    {
        List<string> left = [.. recorded];
        List<string> right = [.. replayed];
        foreach (string item in left.ToList())
        {
            if (right.Remove(item))
            {
                left.Remove(item);
            }
        }

        differences.AddRange(left.Select(item => $"Recorded {kind} missing from the replay: {item}"));
        differences.AddRange(right.Select(item => $"Replay added a {kind}: {item}"));
    }
}
