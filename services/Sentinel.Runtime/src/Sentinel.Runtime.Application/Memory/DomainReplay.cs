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
using Sentinel.Runtime.Application.Abstractions.Persistence;
using Sentinel.Runtime.Application.Options;
using Sentinel.Runtime.Application.Projections;
using Sentinel.Runtime.Domain.Incidents.Events;

namespace Sentinel.Runtime.Application.Memory;

/// <summary>
/// The pure part of level-1 domain replay (Phase 07): a hash of everything an incident's stream
/// establishes, and every recorded decision re-run on the stream prefix it was made from.
/// </summary>
public static class DomainReplay
{
    private const double ConfidenceTolerance = 1e-9;

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

    /// <summary>Each recorded decision beside the decision the same inputs produce now.</summary>
    public static IReadOnlyList<DecisionReplay> Decisions(IReadOnlyList<StoredEvent> history, ConsensusSettings current)
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
            // Each decision replays by the algorithm it was made with, so a newer algorithm never rewrites history.
            bool consensus = recorded.AlgorithmVersion is ConsensusSettings.AlgorithmV1 or ConsensusSettings.AlgorithmVersion;
            Decision replayed = consensus
                ? IncidentCommandProcessor.Evaluate(prefix, true, recorded.Settings ?? current, recorded.DecisionId, recorded.DecidedAt, recorded.AlgorithmVersion)
                : IncidentCommandProcessor.Evaluate(prefix, false, recorded.Settings ?? current, recorded.DecisionId, recorded.DecidedAt);
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
