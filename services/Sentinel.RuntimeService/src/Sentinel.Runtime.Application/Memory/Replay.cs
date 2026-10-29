using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using Sentinel.Contracts.Incidents;
using Sentinel.Contracts.Messaging;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Runtime.Application.Incidents;
using Sentinel.Runtime.Domain.Incidents;

namespace Sentinel.Runtime.Application.Memory;

/// <summary>
/// Level-1 domain replay (Phase 07): rebuilds an incident's state and projection from its event
/// stream and re-runs every recorded decision on the stream prefix it was made from. It reads the
/// stream and the stored projection and nothing else: it writes no event, sends no message, signals
/// no workflow and calls no provider or model, so it can run against any incident at any time.
/// </summary>
public sealed class DomainReplayService(IIncidentStore store, TimeProvider time, IOptions<RuntimeOptions> options)
{
    private const double ConfidenceTolerance = 1e-9;

    public async Task<DomainReplayResult?> ReplayAsync(IncidentId incidentId, CancellationToken cancellationToken)
    {
        IReadOnlyList<StoredEvent> history = await store.ReadStreamAsync(incidentId, cancellationToken);
        if (history.Count == 0)
        {
            return null;
        }

        string hash = StateHash(Incident.Rehydrate(history.Select(stored => stored.Event)));
        bool stable = hash == StateHash(Incident.Rehydrate(history.Select(stored => stored.Event)));

        IncidentDetail rebuilt = IncidentProjector.Project(history);
        IncidentDetail? stored = await store.GetDetailAsync(incidentId, cancellationToken);
        IReadOnlyList<string> differences = stored is null
            ? ["No projection is stored for the incident."]
            : JsonDifferences.Between(JsonSerializer.SerializeToNode(stored, SentinelJson.Options), JsonSerializer.SerializeToNode(rebuilt, SentinelJson.Options), 25);

        return new DomainReplayResult
        {
            IncidentId = incidentId,
            StreamVersion = history[^1].Version,
            EventCount = history.Count,
            StateHash = hash,
            StateStable = stable,
            ProjectionMatches = differences.Count == 0,
            ProjectionDifferences = differences,
            Decisions = ReplayDecisions(history, options.Value.ConsensusSettings()),
            ReplayedAt = time.GetUtcNow()
        };
    }

    /// <summary>SHA-256 over the canonical JSON of everything the stream establishes.</summary>
    public static string StateHash(Incident incident)
    {
        ArgumentNullException.ThrowIfNull(incident);

        var state = new
        {
            Snapshot = IncidentSnapshot.Of(incident),
            incident.Severity,
            incident.Changes,
            incident.LinkedSignals,
            Reports = incident.Reports.Select(reported => reported.Report),
            incident.Decisions,
            incident.Plan,
            incident.Approval,
            incident.Rejection,
            incident.Execution,
            incident.ExecutionStart,
            incident.ExecutionOutcome,
            incident.RecoveryWindow,
            incident.Recovery,
            incident.ClosingReason,
            incident.ResolvedAt
        };
        return Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(state, SentinelJson.Options)));
    }

    private static List<DecisionReplay> ReplayDecisions(IReadOnlyList<StoredEvent> history, ConsensusSettings current)
    {
        var replays = new List<DecisionReplay>();
        for (int index = 0; index < history.Count; index++)
        {
            if (history[index].Event is not DecisionMade made)
            {
                continue;
            }

            Decision recorded = made.Decision;
            var prefix = Incident.Rehydrate(history.Take(index).Select(stored => stored.Event));
            bool consensus = recorded.AlgorithmVersion == ConsensusSettings.AlgorithmVersion;
            Decision replayed = IncidentCommandProcessor.Evaluate(prefix, consensus, recorded.Settings ?? current, recorded.DecisionId, recorded.DecidedAt);
            bool matches = replayed.Result == recorded.Result
                && replayed.LeadingHypothesisId == recorded.LeadingHypothesisId
                && Math.Abs(replayed.Confidence - recorded.Confidence) < ConfidenceTolerance
                && (recorded.InputsHash is null || replayed.InputsHash == recorded.InputsHash);
            replays.Add(new DecisionReplay(
                recorded.DecisionId,
                history[index].Version,
                recorded.AlgorithmVersion,
                recorded.Settings is not null,
                recorded.Result.ToContract(),
                replayed.Result.ToContract(),
                recorded.LeadingHypothesisId,
                replayed.LeadingHypothesisId,
                recorded.Confidence,
                replayed.Confidence,
                recorded.InputsHash,
                replayed.InputsHash,
                matches));
        }

        return replays;
    }
}

/// <summary>Structural differences between two JSON documents, as JSON paths.</summary>
public static class JsonDifferences
{
    public static IReadOnlyList<string> Between(JsonNode? expected, JsonNode? actual, int limit)
    {
        var differences = new List<string>();
        Compare("$", expected, actual, differences, limit);
        return differences;
    }

    private static void Compare(string path, JsonNode? expected, JsonNode? actual, List<string> differences, int limit)
    {
        if (differences.Count >= limit)
        {
            return;
        }

        switch (expected, actual)
        {
            case (null, null):
                return;
            case (JsonObject left, JsonObject right):
                foreach (string key in left.Select(pair => pair.Key).Union(right.Select(pair => pair.Key)).Order(StringComparer.Ordinal))
                {
                    Compare($"{path}.{key}", left[key], right[key], differences, limit);
                }

                return;
            case (JsonArray left, JsonArray right):
                if (left.Count != right.Count)
                {
                    differences.Add(string.Create(CultureInfo.InvariantCulture, $"{path}: {left.Count} item(s) stored, {right.Count} rebuilt"));
                    return;
                }

                for (int index = 0; index < left.Count; index++)
                {
                    Compare(string.Create(CultureInfo.InvariantCulture, $"{path}[{index}]"), left[index], right[index], differences, limit);
                }

                return;
            default:
                if (!JsonNode.DeepEquals(expected, actual))
                {
                    differences.Add($"{path}: stored {Short(expected)}, rebuilt {Short(actual)}");
                }

                return;
        }
    }

    private static string Short(JsonNode? node)
    {
        string text = node?.ToJsonString() ?? "null";
        return text.Length <= 80 ? text : $"{text[..77]}...";
    }
}
