using System.Globalization;
using System.Text.Json;
using Sentinel.AgentWorker.Application.Tools;
using Sentinel.AgentWorker.Domain.Executions;
using Sentinel.Contracts;
using Sentinel.Contracts.Investigations;
using Sentinel.Contracts.Messaging;
using Sentinel.Contracts.Observations;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Contracts.Queries;
using Sentinel.AgentWorker.Application.Abstractions.Providers;

namespace Sentinel.AgentWorker.Application.Investigations;

/// <summary>
/// The adversarial verifier (Phase 05): for each hypothesis on the blackboard it asks the question
/// that would prove it wrong and reads the telemetry for the answer, then looks for strong signals
/// no hypothesis explains. Its falsifiers are deterministic and read through the same query contract
/// as every tool, so a challenge is reproducible from its recorded tool calls. It never decides:
/// Runtime's consensus weighs its challenges with the rest of the evidence.
/// </summary>
public sealed class AdversarialVerifier(IObservationQueries observations, TimeProvider time) : IInvestigator
{
    public const string InvestigatorName = "adversarial-verifier";
    public const string PromptVersion = "falsifiers-v1";

    public const string ChangeServing = "change-serving-before-onset";
    public const string MechanismFollowsChange = "mechanism-follows-change";
    public const string CulpritPrecedesOnset = "culprit-shift-precedes-onset";
    public const string LoadRose = "load-rose-before-onset";
    public const string Untestable = "no-falsifier";

    private static readonly ExecutionModel Code = new("sentinel", InvestigatorName, "1", PromptVersion);

    public string Name => InvestigatorName;

    public async Task<InvestigationRun> InvestigateAsync(InvestigateIncident command, bool shadow, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        DateTimeOffset started = time.GetUtcNow();
        var reads = new Reads(command, observations, time);
        DateTimeOffset onset = command.SymptomOnset ?? command.ObservationWindow.To;
        DeploymentObservation[] deployments = await reads.DeploymentsAsync(cancellationToken);
        Dictionary<ServiceId, MetricObservation[]> connections = await reads.MetricAsync("redis_connections", null, cancellationToken);
        Dictionary<ServiceId, MetricObservation[]> latency = await reads.MetricAsync("latency_p95", null, cancellationToken);
        Dictionary<ServiceId, MetricObservation[]> load = await reads.MetricAsync("request_rate", command.AffectedServiceId, cancellationToken);

        var output = new VerifierFindings(onset);
        foreach (HypothesisFinding hypothesis in command.Hypotheses ?? [])
        {
            if (hypothesis.SuspectedChangeObservationId is { } changeId)
            {
                ChallengeChange(output, hypothesis, changeId, deployments, connections, latency, onset);
            }
            else if (hypothesis.Key is { } key && key.StartsWith("redis-saturation:", StringComparison.Ordinal) && hypothesis.SuspectedServiceId is { } culprit)
            {
                ChallengeCulprit(output, hypothesis, connections.GetValueOrDefault(culprit), "Redis connections", "call_verify_redis_connections", onset);
            }
            else if (hypothesis.Key is { } dependencyKey && dependencyKey.StartsWith("dependency-regression:", StringComparison.Ordinal) && hypothesis.SuspectedServiceId is { } dependency)
            {
                ChallengeCulprit(output, hypothesis, latency.GetValueOrDefault(dependency), "p95 latency", "call_verify_latency_p95", onset);
            }
            else if (hypothesis.Key is { } loadKey && loadKey.StartsWith("load-change:", StringComparison.Ordinal))
            {
                ChallengeLoad(output, hypothesis, load.GetValueOrDefault(command.AffectedServiceId), onset);
            }
            else
            {
                output.Challenge(hypothesis.HypothesisId, Untestable, ChallengeVerdict.Inconclusive, "No falsification test applies to this explanation; it stands or falls on its evidence.");
            }
        }

        SearchAlternatives(output, command, connections, latency, deployments, onset);

        AgentExecution execution = InvestigationRecords.Start(command, Name, shadow, Code, started);
        foreach (ToolCallRecord call in reads.ToolCalls)
        {
            execution.RecordToolCall(call);
        }

        InvestigationRecords.Finish(execution, InvestigationOutcome.Completed, null, time.GetUtcNow());
        InvestigationRun run = InvestigationRecords.Build(command, execution, output.Evidence, output.Alternatives);
        return run.WithCompleted(run.Completed with { Challenges = output.Challenges });
    }

    /// <summary>F1 and F2: the change must serve before onset, and its own service must shift after it.</summary>
    private static void ChallengeChange(
        VerifierFindings output,
        HypothesisFinding hypothesis,
        ObservationId changeId,
        DeploymentObservation[] deployments,
        Dictionary<ServiceId, MetricObservation[]> connections,
        Dictionary<ServiceId, MetricObservation[]> latency,
        DateTimeOffset onset)
    {
        DeploymentObservation? reported = deployments.FirstOrDefault(deployment => deployment.ObservationId == changeId);
        if (reported is null)
        {
            output.Challenge(hypothesis.HypothesisId, ChangeServing, ChallengeVerdict.Inconclusive, $"The suspected change {changeId} was not found in the window.");
            return;
        }

        DeploymentObservation[] reports = [.. deployments.Where(deployment => deployment.DeploymentId == reported.DeploymentId)];
        DateTimeOffset? serving = reports.Where(deployment => deployment.Status == "completed").Select(deployment => deployment.CompletedAt).FirstOrDefault(completed => completed is not null);
        ObservationId[] cited = [.. reports.Select(deployment => deployment.ObservationId)];
        if (serving is null || serving >= onset)
        {
            string statement = serving is { } late
                ? $"{reported.ServiceId} {reported.Version} began serving at {late:O}, after symptoms began at {onset:O}."
                : $"{reported.ServiceId} {reported.Version} was still rolling out when symptoms began at {onset:O}.";
            EvidenceId contradiction = output.Record(hypothesis.HypothesisId, "contradicts", 0.9, cited, reported.StartedAt, serving ?? onset, "call_verify_deployments", $"deployment:{reported.DeploymentId}", statement);
            output.Challenge(hypothesis.HypothesisId, ChangeServing, ChallengeVerdict.Falsified, "The change was not serving before symptoms began, so it cannot have caused them.", contradiction);
            return;
        }

        DateTimeOffset servedAt = serving.Value;
        output.Challenge(hypothesis.HypothesisId, ChangeServing, ChallengeVerdict.Survived, $"{reported.ServiceId} {reported.Version} served from {servedAt:O}, before onset at {onset:O}.");

        // The changed service's own signals must move after the change and before onset.
        Shift?[] shifts =
        [
            connections.GetValueOrDefault(reported.ServiceId) is { } own ? Shift.Of(own, onset, 30, 2) : null,
            latency.GetValueOrDefault(reported.ServiceId) is { } slow ? Shift.Of(slow, onset, 0.2, 3) : null
        ];
        Shift? shift = shifts
            .OfType<Shift>()
            .Where(candidate => candidate.Start.EventTime >= servedAt - TimeSpan.FromSeconds(10))
            .OrderBy(candidate => candidate.Start.EventTime)
            .FirstOrDefault();
        if (shift is not null)
        {
            EvidenceId support = output.Record(hypothesis.HypothesisId, "supports", 0.5, [shift.Start.ObservationId, shift.Peak.ObservationId], shift.Start.EventTime, shift.Peak.EventTime, "call_verify_signals", Group(shift.Start.MetricName, reported.ServiceId),
                $"{reported.ServiceId} {shift.Start.MetricName} moved from {shift.Baseline:G4} to {shift.Peak.Value:G4} after {reported.Version} began serving.");
            output.Challenge(hypothesis.HypothesisId, MechanismFollowsChange, ChallengeVerdict.Survived, $"{reported.ServiceId}'s own {shift.Start.MetricName} shifted after the change and before onset.", support);
            return;
        }

        MetricObservation[]? flat = connections.GetValueOrDefault(reported.ServiceId) ?? latency.GetValueOrDefault(reported.ServiceId);
        if (flat is { Length: >= 2 })
        {
            EvidenceId weak = output.Record(hypothesis.HypothesisId, "contradicts", 0.4, [flat[0].ObservationId, flat[^1].ObservationId], flat[0].EventTime, flat[^1].EventTime, "call_verify_signals", Group(flat[0].MetricName, reported.ServiceId),
                $"{reported.ServiceId}'s connections and latency showed no shift after {reported.Version} began serving.");
            output.Challenge(hypothesis.HypothesisId, MechanismFollowsChange, ChallengeVerdict.Inconclusive, $"No signal of {reported.ServiceId} moved after the change; a mechanism outside these signals would be needed.", weak);
        }
        else
        {
            output.Challenge(hypothesis.HypothesisId, MechanismFollowsChange, ChallengeVerdict.Inconclusive, $"{reported.ServiceId} reports neither connections nor latency to test the mechanism.");
        }
    }

    /// <summary>F3: a named culprit's own signal must shift before onset.</summary>
    private static void ChallengeCulprit(VerifierFindings output, HypothesisFinding hypothesis, MetricObservation[]? series, string signal, string toolCallId, DateTimeOffset onset)
    {
        if (series is not { Length: >= 4 })
        {
            output.Challenge(hypothesis.HypothesisId, CulpritPrecedesOnset, ChallengeVerdict.Inconclusive, $"No {signal} series for {hypothesis.SuspectedServiceId} to test against.");
            return;
        }

        bool latency = signal.Contains("latency", StringComparison.Ordinal);
        if (Shift.Of(series, onset, latency ? 0.2 : 30, latency ? 3 : 2) is { } shift)
        {
            EvidenceId support = output.Record(hypothesis.HypothesisId, "supports", 0.5, [shift.Start.ObservationId, shift.Peak.ObservationId], shift.Start.EventTime, shift.Peak.EventTime, toolCallId, Group(shift.Start.MetricName, hypothesis.SuspectedServiceId!.Value),
                $"{hypothesis.SuspectedServiceId} {signal} began rising at {shift.Start.EventTime:O}, before onset at {onset:O}.");
            output.Challenge(hypothesis.HypothesisId, CulpritPrecedesOnset, ChallengeVerdict.Survived, $"{hypothesis.SuspectedServiceId}'s {signal} shifted before symptoms began.", support);
            return;
        }

        EvidenceId contradiction = output.Record(hypothesis.HypothesisId, "contradicts", 0.8, [series[0].ObservationId, series[^1].ObservationId], series[0].EventTime, series[^1].EventTime, toolCallId, Group(series[0].MetricName, hypothesis.SuspectedServiceId!.Value),
            $"{hypothesis.SuspectedServiceId} {signal} stayed between {series.Min(sample => sample.Value):G4} and {series.Max(sample => sample.Value):G4} up to onset.");
        output.Challenge(hypothesis.HypothesisId, CulpritPrecedesOnset, ChallengeVerdict.Falsified, $"{hypothesis.SuspectedServiceId}'s {signal} did not shift before symptoms began.", contradiction);
    }

    /// <summary>F4: a load explanation needs load to have risen before onset.</summary>
    private static void ChallengeLoad(VerifierFindings output, HypothesisFinding hypothesis, MetricObservation[]? rate, DateTimeOffset onset)
    {
        MetricObservation[] before = [.. (rate ?? []).Where(sample => sample.EventTime <= onset)];
        if (before.Length < 4)
        {
            output.Challenge(hypothesis.HypothesisId, LoadRose, ChallengeVerdict.Inconclusive, "No request-rate series to test a change in load.");
            return;
        }

        double baseline = before.Take(Math.Max(1, before.Length * 3 / 10)).Average(sample => sample.Value);
        double recent = before.Skip(before.Length * 7 / 10).Average(sample => sample.Value);
        if (baseline > 0 && recent >= baseline * 1.5)
        {
            output.Challenge(hypothesis.HypothesisId, LoadRose, ChallengeVerdict.Survived, string.Create(CultureInfo.InvariantCulture, $"Load rose from {baseline:G3}/s to {recent:G3}/s before onset."));
            return;
        }

        EvidenceId contradiction = output.Record(hypothesis.HypothesisId, "contradicts", 0.7, [before[0].ObservationId, before[^1].ObservationId], before[0].EventTime, before[^1].EventTime, "call_verify_request_rate", $"request-rate:{hypothesis.SuspectedServiceId}",
            string.Create(CultureInfo.InvariantCulture, $"Request rate stayed near {baseline:G3}/s (recently {recent:G3}/s) up to onset."));
        output.Challenge(hypothesis.HypothesisId, LoadRose, ChallengeVerdict.Falsified, "Load did not rise before symptoms began.", contradiction);
    }

    /// <summary>F5: a strong signal that no hypothesis explains is an alternative explanation, added for consensus to weigh.</summary>
    private static void SearchAlternatives(
        VerifierFindings output,
        InvestigateIncident command,
        Dictionary<ServiceId, MetricObservation[]> connections,
        Dictionary<ServiceId, MetricObservation[]> latency,
        DeploymentObservation[] deployments,
        DateTimeOffset onset)
    {
        IReadOnlyList<HypothesisFinding> hypotheses = command.Hypotheses ?? [];
        HashSet<ServiceId> explained = [.. hypotheses.Select(hypothesis => hypothesis.SuspectedServiceId).OfType<ServiceId>()];
        foreach ((ServiceId service, MetricObservation[] series) in connections.Where(entry => !explained.Contains(entry.Key)))
        {
            if (Shift.Of(series, onset, 30, 2) is { } surge && !deployments.Any(deployment => deployment.ServiceId == service))
            {
                output.Alternative(HypothesisKeys.RedisSaturation(service), service, $"{service} opened Redis connections until Redis was saturated.", surge, "call_verify_redis_connections",
                    $"{service} Redis connections rose from {surge.Baseline:G4} to {surge.Peak.Value:G4} before onset, and no hypothesis explains it.");
            }
        }

        foreach ((ServiceId service, MetricObservation[] series) in latency.Where(entry => !explained.Contains(entry.Key) && entry.Key != command.AffectedServiceId))
        {
            if (Shift.Of(series, onset, 0.2, 3) is { } regression)
            {
                output.Alternative(HypothesisKeys.DependencyRegression(service), service, $"{service} slowed down, and {command.AffectedServiceId} timed out waiting for it.", regression, "call_verify_latency_p95",
                    $"{service} p95 latency rose from {regression.Baseline:G3} s to {regression.Peak.Value:G3} s before onset, and no hypothesis explains it.");
            }
        }
    }

    /// <summary>
    /// The verifier re-reads the specialists' signals, so its evidence joins the signal's own independence
    /// group: a second reading of one series is never a second, independent family (Phase 05).
    /// </summary>
    private static string Group(string metric, ServiceId service) => metric switch
    {
        "redis_connections" => $"redis-connections:{service}",
        "latency_p95" => $"latency:{service}",
        "request_rate" => $"request-rate:{service}",
        _ => $"{metric}:{service}"
    };

    /// <summary>A material rise that began before onset: the verifier's own, independent reading of a series.</summary>
    private sealed record Shift(double Baseline, MetricObservation Start, MetricObservation Peak)
    {
        public static Shift? Of(MetricObservation[] series, DateTimeOffset onset, double minimumRise, double minimumRatio)
        {
            if (series.Length < 4)
            {
                return null;
            }

            double baseline = series.Take(Math.Max(1, series.Length * 3 / 10)).Min(sample => sample.Value);
            MetricObservation[] considered = [.. series.Where(sample => sample.EventTime <= onset + TimeSpan.FromSeconds(30))];
            if (considered.Length == 0)
            {
                return null;
            }

            MetricObservation peak = considered.MaxBy(sample => sample.Value)!;
            double rise = peak.Value - baseline;
            if (rise < minimumRise || peak.Value < minimumRatio * Math.Max(baseline, minimumRise / 10))
            {
                return null;
            }

            MetricObservation start = considered.First(sample => sample.Value >= baseline + rise * 0.2);

            // A shift that had already subsided by onset cannot explain symptoms at onset.
            MetricObservation atOnset = considered.Where(sample => sample.EventTime <= onset + TimeSpan.FromSeconds(10)).MaxBy(sample => sample.EventTime) ?? peak;
            return start.EventTime < onset && atOnset.Value >= baseline + rise * 0.5 ? new Shift(baseline, start, peak) : null;
        }
    }

    /// <summary>The verifier's reads, each recorded as a tool call so its challenges cite their provenance.</summary>
    private sealed class Reads(InvestigateIncident command, IObservationQueries observations, TimeProvider time)
    {
        public List<ToolCallRecord> ToolCalls { get; } = [];

        public async Task<DeploymentObservation[]> DeploymentsAsync(CancellationToken cancellationToken)
        {
            ObservationQueryResult result = await QueryAsync("call_verify_deployments", RecentDeploymentsTool.Name, ObservationKind.Deployment, null, null, cancellationToken);
            return [.. result.Observations.OfType<DeploymentObservation>()];
        }

        public async Task<Dictionary<ServiceId, MetricObservation[]>> MetricAsync(string metric, ServiceId? service, CancellationToken cancellationToken)
        {
            ObservationQueryResult result = await QueryAsync($"call_verify_{metric}", MetricRangeTool.Name, ObservationKind.Metric, metric, service, cancellationToken);
            return result.Observations.OfType<MetricObservation>()
                .GroupBy(sample => sample.ServiceId)
                .ToDictionary(group => group.Key, group => group.OrderBy(sample => sample.EventTime).ToArray());
        }

        private async Task<ObservationQueryResult> QueryAsync(string callId, string tool, ObservationKind kind, string? metric, ServiceId? service, CancellationToken cancellationToken)
        {
            DateTimeOffset started = time.GetUtcNow();
            ObservationQueryResult result = await observations.QueryAsync(new ObservationQuery
            {
                TenantId = command.TenantId,
                EnvironmentId = command.EnvironmentId,
                ServiceId = service,
                Kind = kind,
                Range = command.ObservationWindow,
                Filters = metric is null ? new Dictionary<string, string>() : new Dictionary<string, string> { ["metricName"] = metric },
                Limit = 4000
            }, cancellationToken);
            ToolCalls.Add(new ToolCallRecord
            {
                ToolCallId = callId,
                ToolName = tool,
                Arguments = JsonSerializer.Serialize(new { metric, service = service?.Value }, SentinelJson.Options),
                Result = JsonSerializer.Serialize(result.Observations.Select(observation => observation.ObservationId), SentinelJson.Options),
                ObservationIds = [.. result.Observations.Select(observation => observation.ObservationId)],
                Partial = result.Metadata.Completeness != QueryCompleteness.Complete || result.Metadata.Truncated,
                StartedAt = started,
                Duration = time.GetUtcNow() - started
            });
            return result;
        }
    }

    private sealed class VerifierFindings(DateTimeOffset onset)
    {
        private readonly List<EvidenceFinding> _evidence = [];
        private readonly List<ChallengeFinding> _challenges = [];
        private readonly List<HypothesisFinding> _alternatives = [];

        public IReadOnlyList<EvidenceFinding> Evidence => _evidence;

        public IReadOnlyList<ChallengeFinding> Challenges => _challenges;

        public IReadOnlyList<HypothesisFinding> Alternatives => _alternatives;

        public EvidenceId Record(HypothesisId hypothesis, string direction, double strength, IReadOnlyList<ObservationId> cited, DateTimeOffset from, DateTimeOffset to, string toolCallId, string group, string statement)
        {
            var id = EvidenceId.New();
            _evidence.Add(new EvidenceFinding
            {
                EvidenceId = id,
                Statement = statement,
                Direction = direction == "contradicts" ? EvidenceDirection.Contradicts : EvidenceDirection.Supports,
                Strength = strength,
                SourceObservationIds = [.. cited.Distinct()],
                HypothesisIds = [hypothesis],
                EventTimeRange = new TimeRange(from, to < from ? from : to),
                ToolCallId = toolCallId,
                IndependenceGroup = group
            });
            return id;
        }

        public void Challenge(HypothesisId hypothesis, string falsifier, ChallengeVerdict verdict, string detail, params EvidenceId[] evidence) =>
            _challenges.Add(new ChallengeFinding { HypothesisId = hypothesis, Falsifier = falsifier, Verdict = verdict, Detail = detail, EvidenceIds = evidence });

        public void Alternative(string key, ServiceId service, string statement, Shift shift, string toolCallId, string evidenceStatement)
        {
            var id = HypothesisId.New();
            EvidenceId support = Record(id, "supports", 0.7, [shift.Start.ObservationId, shift.Peak.ObservationId], shift.Start.EventTime, shift.Peak.EventTime, toolCallId, Group(shift.Start.MetricName, service), evidenceStatement);
            _alternatives.Add(new HypothesisFinding
            {
                HypothesisId = id,
                Key = key,
                Statement = statement,
                CausalChain = [$"{service} {shift.Start.MetricName} shift", "symptom", "SLO burn"],
                SuspectedServiceId = service,
                SupportingEvidenceIds = [support]
            });
            Challenge(id, CulpritPrecedesOnset, ChallengeVerdict.Survived, $"Found by the verifier: {service} shifted at {shift.Start.EventTime:O}, before onset at {onset:O}.", support);
        }
    }
}
