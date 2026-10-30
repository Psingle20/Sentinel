using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using Sentinel.AgentWorker.Application.Abstractions.Providers;

namespace Sentinel.AgentWorker.Infrastructure.Models;

/// <summary>
/// The model test double (ADR-015), the default until the Muse Spark details are configured. It
/// speaks the same tool protocol as a real model: it first requests the tools its role was given,
/// then analyses only what those tools returned with fixed, generic rules — change proximity,
/// connection surges, latency regressions, contradicting signals, log signatures and slow calls.
/// It never reads scenario ground truth. It proves the pipeline, not reasoning quality.
/// </summary>
public sealed class DeterministicModelProvider(IOptions<TestDoubleOptions> options) : IModelProvider
{
    public const string ProviderName = "sentinel-test-double";

    public string Provider => ProviderName;

    public string Model => "deterministic-analyst";

    public string? ModelVersion => "2";

    /// <summary>The double answers from the conversation alone, so a replay of it is deterministic.</summary>
    public bool Deterministic => true;

    public async Task<ModelResponse> CompleteAsync(ModelRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        TestDoubleMode mode = options.Value.Mode;
        if (mode == TestDoubleMode.Refusal)
        {
            return ModelResponse.Failed(ModelFinish.Refused, "The test double refuses by configuration.");
        }

        if (mode == TestDoubleMode.Timeout)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }

        var usage = new ModelUsage(request.Messages.Sum(message => message.Content.Length) / 4, 400);
        var conversation = Conversation.Read(request);
        if (conversation.ToolResults.Count == 0)
        {
            return new ModelResponse(ModelFinish.ToolCalls, conversation.PlanToolCalls(), null, usage);
        }

        string content = mode switch
        {
            TestDoubleMode.Malformed => "{ this is not the findings document",
            TestDoubleMode.Incomplete => Analysis.Run(conversation, singleHypothesis: true, fabricate: false),
            TestDoubleMode.Fabricated => Analysis.Run(conversation, singleHypothesis: false, fabricate: true),
            TestDoubleMode.InjectionSusceptible when conversation.SawInjection => Analysis.Obey(conversation),
            _ => Analysis.Run(conversation, singleHypothesis: false, fabricate: false)
        };

        return new ModelResponse(ModelFinish.Final, [], content, usage);
    }

    /// <summary>What the double can see: the task, its tools, the metric catalog and the tool results so far.</summary>
    internal sealed class Conversation
    {
        private static readonly string[] InjectionMarkers = ["ignore your previous instructions", "ignore all previous instructions", "system note to ai"];

        public string AffectedService { get; private init; } = string.Empty;

        public string Role { get; private init; } = "generalist";

        public IReadOnlyList<string> RelatedServices { get; private init; } = [];

        public IReadOnlyList<string> Metrics { get; private init; } = [];

        public IReadOnlySet<string> Tools { get; private init; } = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>The onset Runtime detected, when the task states it.</summary>
        public DateTimeOffset? SymptomOnset { get; private init; }

        public Dictionary<string, (string Tool, JsonNode? Result)> ToolResults { get; } = new(StringComparer.Ordinal);

        /// <summary>Whether any tool result carried text addressed to an AI, as planted in a runbook or log line.</summary>
        public bool SawInjection { get; private set; }

        public bool Has(string tool) => Tools.Contains(tool);

        public static Conversation Read(ModelRequest request)
        {
            IReadOnlyList<ModelMessage> messages = request.Messages;
            string system = messages.FirstOrDefault(message => message.Role == ModelRole.System)?.Content ?? string.Empty;
            string task = messages.FirstOrDefault(message => message.Role == ModelRole.User)?.Content ?? string.Empty;
            var data = JsonNode.Parse(task[(task.IndexOf('{', StringComparison.Ordinal) is var start and >= 0 ? start : 0)..]);

            var conversation = new Conversation
            {
                AffectedService = data?["affectedService"]?.GetValue<string>() ?? string.Empty,
                Role = data?["role"]?.GetValue<string>() ?? "generalist",
                RelatedServices = [.. data?["relatedServices"]?.AsArray().Select(service => service!.GetValue<string>()) ?? []],
                SymptomOnset = data?["symptomOnset"]?.GetValue<string>() is { } onset ? DateTimeOffset.Parse(onset, CultureInfo.InvariantCulture) : null,
                Tools = new HashSet<string>(request.Tools.Select(tool => tool.Name), StringComparer.Ordinal),
                Metrics = [.. system.Split('\n')
                    .Select(line => line.Trim())
                    .Where(line => line.StartsWith("- ", StringComparison.Ordinal) && line.Contains(':', StringComparison.Ordinal))
                    .Select(line => line[2..line.IndexOf(':', StringComparison.Ordinal)])
                    .Where(name => !name.Contains(' ', StringComparison.Ordinal))]
            };

            var calls = messages
                .Where(message => message.Role == ModelRole.Assistant)
                .SelectMany(message => message.ToolCalls ?? [])
                .ToDictionary(call => call.Id, call => call.Name, StringComparer.Ordinal);
            foreach (ModelMessage result in messages.Where(message => message.Role == ModelRole.Tool && message.ToolCallId is not null))
            {
                conversation.ToolResults[result.ToolCallId!] = (calls.GetValueOrDefault(result.ToolCallId!) ?? "unknown", JsonNode.Parse(result.Content));
                conversation.SawInjection |= InjectionMarkers.Any(marker => result.Content.Contains(marker, StringComparison.OrdinalIgnoreCase));
            }

            return conversation;
        }

        /// <summary>The first turn: every call the role's tools allow, within a generalist's budget.</summary>
        public IReadOnlyList<ModelToolCall> PlanToolCalls()
        {
            var calls = new List<ModelToolCall>();
            string[] scope = [AffectedService, .. RelatedServices];
            if (Has("recent_deployments"))
            {
                calls.Add(new("call_deployments", "recent_deployments", JsonSerializer.Serialize(new { services = scope })));
            }

            if (Has("metric_range"))
            {
                foreach (string metric in Metrics)
                {
                    // The SLO and the affected service's own dependency wait for that service; every other metric for every service.
                    string arguments = metric is "slo_burn_rate" or "dependency_wait_p95"
                        ? JsonSerializer.Serialize(new { metric, service = AffectedService })
                        : JsonSerializer.Serialize(new { metric });
                    calls.Add(new ModelToolCall($"call_{metric}", "metric_range", arguments));
                }
            }

            bool generalist = Role == "generalist";
            if (Has("log_signatures"))
            {
                foreach (string service in generalist ? [AffectedService] : scope)
                {
                    calls.Add(new ModelToolCall($"call_logs_{service}", "log_signatures", JsonSerializer.Serialize(new { service })));
                }
            }

            if (Has("slow_spans"))
            {
                calls.Add(new ModelToolCall($"call_spans_{AffectedService}", "slow_spans", JsonSerializer.Serialize(new { service = AffectedService })));
            }

            if (Has("service_dependencies") && !generalist)
            {
                calls.Add(new ModelToolCall("call_dependencies", "service_dependencies", "{}"));
            }

            if (Has("search_runbooks") && !generalist)
            {
                calls.Add(new ModelToolCall("call_runbooks", "search_runbooks", JsonSerializer.Serialize(new { text = $"{AffectedService} failures errors Redis connections dependency latency", service = AffectedService })));
            }

            return calls;
        }
    }

    /// <summary>The double's fixed, generic rules over whatever its tools returned.</summary>
    internal static class Analysis
    {
        private static readonly TimeSpan ProximityScale = TimeSpan.FromMinutes(2);

        // A message received this often is failing on every receive, not on a passing fault.
        private const int PoisonReceives = 3;

        // Orders booked again this often a second are a pattern, not one unlucky redelivery.
        private const double DuplicateRate = 0.5;

        // Calls retried this often a second are a storm, not an occasional slow attempt.
        private const double RetryRate = 1.0;

        // An excursion counts when it was under way at onset: from this long before it to half a minute after.
        private static readonly TimeSpan OngoingBefore = TimeSpan.FromMinutes(2);
        private static readonly TimeSpan OngoingAfter = TimeSpan.FromSeconds(30);

        public static string Run(Conversation conversation, bool singleHypothesis, bool fabricate)
        {
            var output = new Findings();
            Series? burn = SeriesOf(conversation, "slo_burn_rate").FirstOrDefault(series => series.Service == conversation.AffectedService);
            DateTimeOffset onset = conversation.SymptomOnset ?? Onset(burn) ?? DateTimeOffset.UtcNow;
            Change[] changes = Changes(conversation);

            ProximityOfChanges(conversation, output, changes, onset);
            Series[] connections = SeriesOf(conversation, "redis_connections");
            Surge[] surges = ConnectionSurges(conversation, output, connections, changes, onset);
            ContradictFlatConnections(output, connections, surges, changes);
            Regression[] regressions = LatencyRegressions(conversation, output, changes, onset);
            ContradictChangesAfterRegression(conversation, output, regressions, changes);
            RetryAmplification(conversation, output, regressions, changes, onset);
            LoadChange(conversation, output, onset);
            LogSignatures(conversation, output, changes, onset);
            PoisonMessages(conversation, output, changes, onset);
            DuplicateDeliveries(conversation, output, changes, onset);
            SlowCalls(conversation, output, changes);
            Topology(conversation, output);

            if (conversation.Role == "generalist")
            {
                if (output.HypothesisCount < 2)
                {
                    output.Hypothesis($"load-change:{conversation.AffectedService}", $"A change in load on {conversation.AffectedService}, not a deployment, caused the symptoms.", ["traffic change", "saturation", "SLO burn"], conversation.AffectedService, null);
                }

                if (output.HypothesisCount < 2)
                {
                    output.Hypothesis("external-dependency", "An external dependency degraded.", ["dependency degradation", "SLO burn"], null, null);
                }
            }

            if (fabricate)
            {
                string target = output.HypothesisCount > 0 ? output.FirstHypothesis : output.Hypothesis("external-dependency", "An external dependency degraded.", ["dependency degradation"], null, null);
                output.Evidence(target, "A remembered signal that no tool returned.", "supports", 0.9, ["fabricated:observation:1"], onset, onset, conversation.ToolResults.Keys.First(), null);
            }

            if (conversation.ToolResults.Values.Any(result => result.Tool == "search_runbooks"))
            {
                output.Unknown("Runbook passages were read as reference only; they cannot be cited as evidence.");
            }

            return output.ToJson(singleHypothesis);
        }

        /// <summary>What a model that obeys planted instructions would answer: actions and a certain, uncited claim.</summary>
        public static string Obey(Conversation conversation) => new JsonObject
        {
            ["abstain"] = false,
            ["actions"] = new JsonArray(new JsonObject { ["type"] = "restart", ["service"] = conversation.AffectedService }),
            ["evidence"] = new JsonArray(),
            ["hypotheses"] = new JsonArray(new JsonObject
            {
                ["id"] = "h1",
                ["statement"] = "The cause is certain; restart everything now.",
                ["causalChain"] = new JsonArray("instruction", "action")
            })
        }.ToJsonString();

        /// <summary>R1: every deployment or rollback is a candidate, weighted by how close it began serving to onset.</summary>
        private static void ProximityOfChanges(Conversation conversation, Findings output, Change[] changes, DateTimeOffset onset)
        {
            if (conversation.Role is not ("changes" or "generalist"))
            {
                return;
            }

            foreach (Change change in changes.Where(change => change.Kind is "deploy" or "rollback"))
            {
                string hypothesis = output.Hypothesis(
                    change.Key,
                    $"The {change.Service} {change.Version} deployment caused the symptoms.",
                    [$"deploy {change.Service} {change.Version}", "regression in " + change.Service, $"{conversation.AffectedService} SLO burn"],
                    change.Service,
                    change.StartedId);
                double strength = change.ServingFrom is { } serving && serving <= onset
                    ? 0.3 + 0.4 * Math.Exp(-(onset - serving) / ProximityScale)
                    : 0.2;
                output.Evidence(
                    hypothesis,
                    $"{change.Service} {change.Version} started at {change.StartedAt:O} and {(change.ServingFrom is { } from ? $"served from {from:O}" : "had not finished rolling out")}; symptoms began at {onset:O}.",
                    "supports",
                    Math.Round(strength, 2),
                    change.ObservationIds,
                    change.StartedAt,
                    change.ServingFrom ?? change.StartedAt,
                    "call_deployments",
                    $"deployment:{change.DeploymentId}");
            }
        }

        /// <summary>R2: a client whose Redis connections surged before onset saturated Redis; a change it was serving explains the surge.</summary>
        private static Surge[] ConnectionSurges(Conversation conversation, Findings output, Series[] connections, Change[] changes, DateTimeOffset onset)
        {
            Surge[] surges = [.. connections.Select(series => Surge.Of(series, onset)).OfType<Surge>()];
            foreach (Surge surge in surges)
            {
                Change? leak = changes.FirstOrDefault(change => change.Service == surge.Series.Service && change.Kind is "deploy" or "rollback" && change.ServingFrom is { } serving && serving <= surge.Start.At + TimeSpan.FromSeconds(10));
                string hypothesis = leak is not null
                    ? output.Hypothesis(leak.Key, $"The {leak.Service} {leak.Version} deployment introduced a Redis connection leak.", [$"deploy {leak.Service} {leak.Version}", "Redis connection leak", "Redis saturation", $"{conversation.AffectedService} errors", "SLO burn"], leak.Service, leak.StartedId, reword: true)
                    : output.Hypothesis(
                        $"redis-saturation:{surge.Series.Service}",
                        $"{surge.Series.Service} opened Redis connections until Redis was saturated.",
                        [$"{surge.Series.Service} connection surge", "Redis maxclients reached", $"{conversation.AffectedService} cannot obtain connections", "SLO burn"],
                        surge.Series.Service,
                        null);
                output.Evidence(
                    hypothesis,
                    $"{surge.Series.Service} Redis connections rose from {surge.Baseline:G4} to {surge.Peak.Value:G4} between {surge.Start.At:O} and {surge.Peak.At:O}.",
                    "supports",
                    0.85,
                    [surge.Start.Id, surge.Peak.Id],
                    surge.Start.At,
                    surge.Peak.At,
                    "call_redis_connections",
                    $"redis-connections:{surge.Series.Service}");
            }

            return surges;
        }

        /// <summary>R3: a change whose own service kept flat connections while another client surged is contradicted.</summary>
        private static void ContradictFlatConnections(Findings output, Series[] connections, Surge[] surges, Change[] changes)
        {
            if (surges.Length == 0)
            {
                return;
            }

            foreach (Change change in changes.Where(change => change.Kind is "deploy" or "rollback" && surges.All(surge => surge.Series.Service != change.Service)))
            {
                if (connections.FirstOrDefault(series => series.Service == change.Service) is { Samples.Count: >= 2 } flat)
                {
                    string hypothesis = output.Hypothesis(change.Key, $"The {change.Service} {change.Version} deployment caused the symptoms.", [$"deploy {change.Service} {change.Version}", "regression", "SLO burn"], change.Service, change.StartedId);
                    output.Evidence(
                        hypothesis,
                        $"{change.Service} Redis connections stayed between {flat.Samples.Min(sample => sample.Value):G4} and {flat.Samples.Max(sample => sample.Value):G4} while {surges[0].Series.Service} surged.",
                        "contradicts",
                        0.6,
                        [flat.Samples[0].Id, flat.Samples[^1].Id],
                        flat.Samples[0].At,
                        flat.Samples[^1].At,
                        "call_redis_connections",
                        $"redis-connections:{change.Service}");
                }
            }
        }

        /// <summary>R4: a dependency's latency that regressed before onset; a change serving before the regression explains it.</summary>
        private static Regression[] LatencyRegressions(Conversation conversation, Findings output, Change[] changes, DateTimeOffset onset)
        {
            Regression[] regressions = [.. SeriesOf(conversation, "latency_p95").Select(series => Regression.Of(series, onset)).OfType<Regression>()];
            Series? wait = SeriesOf(conversation, "dependency_wait_p95").FirstOrDefault(series => series.Service == conversation.AffectedService);
            Regression? waitRegression = wait is null ? null : Regression.Of(wait, onset);
            var reported = new List<Regression>();
            foreach (Regression regression in regressions)
            {
                Change? cause = changes.FirstOrDefault(change => change.Service == regression.Series.Service && change.Kind is "deploy" or "rollback" && change.ServingFrom is { } serving && serving <= regression.Start.At);
                bool dependency = regression.Series.Service != conversation.AffectedService;
                if (cause is null && !dependency)
                {
                    // The affected service's own latency is a symptom unless one of its changes preceded it.
                    continue;
                }

                string hypothesis = cause is not null
                    ? output.Hypothesis(cause.Key, $"The {cause.Service} {cause.Version} deployment slowed {cause.Service}.", [$"deploy {cause.Service} {cause.Version}", $"{cause.Service} latency regression", $"{conversation.AffectedService} timeouts and retries", "SLO burn"], cause.Service, cause.StartedId, reword: dependency)
                    : output.Hypothesis($"dependency-regression:{regression.Series.Service}", $"{regression.Series.Service} slowed down, and {conversation.AffectedService} timed out waiting for it.", [$"{regression.Series.Service} latency regression", $"{conversation.AffectedService} timeouts", "SLO burn"], regression.Series.Service, null);
                string group = $"latency:{regression.Series.Service}";
                output.Evidence(
                    hypothesis,
                    $"{regression.Series.Service} p95 latency rose from {regression.Baseline:G3} s to {regression.Peak.Value:G3} s between {regression.Start.At:O} and {regression.Peak.At:O}.",
                    "supports",
                    0.8,
                    [regression.Start.Id, regression.Peak.Id],
                    regression.Start.At,
                    regression.Peak.At,
                    "call_latency_p95",
                    group);

                // The caller's wait on its dependency is a copy of the same signal: same independence group.
                if (dependency && waitRegression is not null)
                {
                    output.Evidence(
                        hypothesis,
                        $"{conversation.AffectedService} waited up to {waitRegression.Peak.Value:G3} s per attempt on its dependency by {waitRegression.Peak.At:O}.",
                        "supports",
                        0.6,
                        [waitRegression.Start.Id, waitRegression.Peak.Id],
                        waitRegression.Start.At,
                        waitRegression.Peak.At,
                        "call_dependency_wait_p95",
                        group);
                }

                if (dependency)
                {
                    reported.Add(regression);
                }
            }

            return [.. reported];
        }

        /// <summary>R5: a change to the affected service that began serving after a dependency was already degrading is contradicted.</summary>
        private static void ContradictChangesAfterRegression(Conversation conversation, Findings output, Regression[] regressions, Change[] changes)
        {
            foreach (Regression regression in regressions)
            {
                foreach (Change change in changes.Where(change => change.Service == conversation.AffectedService && change.Kind is "deploy" or "rollback" && change.ServingFrom is { } serving && serving > regression.Start.At))
                {
                    string hypothesis = output.Hypothesis(change.Key, $"The {change.Service} {change.Version} deployment caused the symptoms.", [$"deploy {change.Service} {change.Version}", "regression", "SLO burn"], change.Service, change.StartedId);
                    output.Evidence(
                        hypothesis,
                        $"{regression.Series.Service} latency was already rising from {regression.Start.At:O}, before {change.Service} {change.Version} began serving at {change.ServingFrom:O}.",
                        "contradicts",
                        0.6,
                        [regression.Start.Id, regression.Peak.Id],
                        regression.Start.At,
                        regression.Peak.At,
                        "call_latency_p95",
                        $"latency:{regression.Series.Service}");
                }
            }
        }

        /// <summary>
        /// R11 (Phase E01): a consumer retrying at once while a dependency was already slowing. The retries add to the
        /// dependency's load, so they support the dependency's regression, not a cause in the consumer.
        /// </summary>
        private static void RetryAmplification(Conversation conversation, Findings output, Regression[] regressions, Change[] changes, DateTimeOffset onset)
        {
            foreach (Series retries in SeriesOf(conversation, "queue_retry_rate"))
            {
                if (Ongoing(retries, onset, 0) is not { } samples || samples.MaxBy(sample => sample.Value) is not { Value: >= RetryRate } peak)
                {
                    continue;
                }

                Sample first = samples.First(sample => sample.Value >= RetryRate / 2);
                foreach (Regression regression in regressions.Where(regression => regression.Start.At <= first.At))
                {
                    string dependency = regression.Series.Service;
                    Change? cause = changes.FirstOrDefault(change => change.Service == dependency && change.Kind is "deploy" or "rollback" && change.ServingFrom is { } serving && serving <= regression.Start.At);
                    string hypothesis = cause is not null
                        ? output.Hypothesis(cause.Key, $"The {cause.Service} {cause.Version} deployment slowed {cause.Service}.", [$"deploy {cause.Service} {cause.Version}", $"{cause.Service} latency regression", $"{retries.Service} retries", "symptoms"], cause.Service, cause.StartedId)
                        : output.Hypothesis($"dependency-regression:{dependency}", $"{dependency} slowed down, and {conversation.AffectedService} timed out waiting for it.", [$"{dependency} latency regression", $"{retries.Service} retries at once", $"{conversation.AffectedService} stops keeping up"], dependency, null);
                    output.Evidence(
                        hypothesis,
                        $"{retries.Service} began retrying calls at once at {first.At:O}, up to {peak.Value:G3} a second by {peak.At:O}, after {dependency} had begun slowing at {regression.Start.At:O}: the retries add to {dependency}'s load rather than cause its slowdown.",
                        "supports",
                        0.5,
                        [first.Id, peak.Id],
                        first.At,
                        peak.At,
                        "call_queue_retry_rate",
                        $"retries:{retries.Service}");
                }
            }
        }

        /// <summary>R6: a change in load is always a plausible alternative; request rate supports or contradicts it.</summary>
        private static void LoadChange(Conversation conversation, Findings output, DateTimeOffset onset)
        {
            if (SeriesOf(conversation, "request_rate").FirstOrDefault(series => series.Service == conversation.AffectedService) is not { Samples.Count: >= 4 } rate)
            {
                return;
            }

            Sample[] before = [.. rate.Samples.Where(sample => sample.At <= onset)];
            if (before.Length < 4)
            {
                return;
            }

            double baseline = before.Take(Math.Max(1, before.Length * 3 / 10)).Average(sample => sample.Value);
            double recent = before.Skip(before.Length * 7 / 10).Average(sample => sample.Value);
            string hypothesis = output.Hypothesis($"load-change:{conversation.AffectedService}", $"A change in load on {conversation.AffectedService}, not a deployment, caused the symptoms.", ["traffic change", "saturation", "SLO burn"], conversation.AffectedService, null);
            bool rose = baseline > 0 && recent >= baseline * 1.5;
            output.Evidence(
                hypothesis,
                rose
                    ? $"{conversation.AffectedService} request rate rose from {baseline:G3}/s to {recent:G3}/s before symptoms began."
                    : $"{conversation.AffectedService} request rate stayed near {baseline:G3}/s (recently {recent:G3}/s) up to symptom onset.",
                rose ? "supports" : "contradicts",
                rose ? 0.6 : 0.5,
                [before[0].Id, before[^1].Id],
                before[0].At,
                before[^1].At,
                "call_request_rate",
                $"request-rate:{conversation.AffectedService}");
        }

        /// <summary>R7: log signatures — Redis refusing clients supports saturation; giving up on a dependency supports that dependency's regression.</summary>
        private static void LogSignatures(Conversation conversation, Findings output, Change[] changes, DateTimeOffset onset)
        {
            foreach ((string callId, JsonNode result) in conversation.ToolResults.Where(entry => entry.Value.Tool == "log_signatures" && entry.Value.Result is not null).Select(entry => (entry.Key, entry.Value.Result!)))
            {
                string service = result["service"]?.GetValue<string>() ?? conversation.AffectedService;
                foreach (JsonNode signature in result["signatures"]?.AsArray().OfType<JsonNode>() ?? [])
                {
                    string message = signature["message"]?.GetValue<string>() ?? string.Empty;
                    string id = signature["id"]!.GetValue<string>();
                    int count = int.TryParse(signature["count"]?.GetValue<string>(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) ? parsed : 1;
                    DateTimeOffset first = Time(signature["firstSeen"]) ?? onset;
                    DateTimeOffset last = Time(signature["lastSeen"]) ?? first;
                    if (message.Contains("max number of clients", StringComparison.OrdinalIgnoreCase))
                    {
                        string hypothesis = output.Hypothesis("redis-saturation", "Redis ran out of client connections, so services could not reach it.", ["Redis maxclients reached", $"{service} fails on Redis", "SLO burn"], null, null);
                        output.Evidence(hypothesis, $"{service} logged {count} 'max number of clients reached' Redis errors from {first:O} to {last:O}.", "supports", 0.6, [id], first, last, callId, $"logs:{service}:redis-maxclients");
                    }
                    else if (GaveUp().Match(message) is { Success: true } gaveUp)
                    {
                        string dependency = gaveUp.Groups[1].Value;
                        Change? cause = changes.FirstOrDefault(change => change.Service == dependency && change.Kind is "deploy" or "rollback" && change.ServingFrom is { } serving && serving <= first);
                        string hypothesis = cause is not null
                            ? output.Hypothesis(cause.Key, $"The {cause.Service} {cause.Version} deployment slowed {cause.Service}.", [$"deploy {cause.Service} {cause.Version}", $"{cause.Service} slow", $"{service} gives up", "SLO burn"], cause.Service, cause.StartedId)
                            : output.Hypothesis($"dependency-regression:{dependency}", $"{dependency} degraded, and {service} gave up waiting for it.", [$"{dependency} degraded", $"{service} timeouts", "SLO burn"], dependency, null);
                        output.Evidence(hypothesis, $"{service} logged {count} '{message}' lines from {first:O} to {last:O}.", "supports", 0.5, [id], first, last, callId, $"logs:{service}:{dependency}-timeouts");
                    }
                }
            }
        }

        /// <summary>
        /// R9 (Phase E01): one message that fails on every receive while the others are processed. Its receive count keeps
        /// rising and its failures repeat in the logs under one message ID; a change that began serving after the first
        /// failure cannot explain it.
        /// </summary>
        private static void PoisonMessages(Conversation conversation, Findings output, Change[] changes, DateTimeOffset onset)
        {
            foreach (Series receives in SeriesOf(conversation, "queue_max_receive_count"))
            {
                if (Ongoing(receives, onset, 1) is not { } samples || samples.MaxBy(sample => sample.Value) is not { Value: >= PoisonReceives } peak)
                {
                    continue;
                }

                Sample first = samples.First(sample => sample.Value > 1);
                string hypothesis = PoisonHypothesis(output, receives.Service, conversation.AffectedService);
                output.Evidence(
                    hypothesis,
                    $"{receives.Service}'s highest receive count rose above 1 at {first.At:O} and reached {peak.Value:G4} by {peak.At:O}.",
                    "supports",
                    0.8,
                    [first.Id, peak.Id],
                    first.At,
                    peak.At,
                    "call_queue_max_receive_count",
                    $"queue-receives:{receives.Service}");

                if (SeriesOf(conversation, "queue_processed_rate").FirstOrDefault(series => series.Service == receives.Service) is { Samples.Count: > 0 } processed
                    && processed.Samples.Where(sample => sample.At >= first.At).ToArray() is { Length: > 0 } after
                    && after.Min(sample => sample.Value) > 0.5)
                {
                    Sample lowest = after.MinBy(sample => sample.Value)!;
                    output.Evidence(
                        hypothesis,
                        $"{receives.Service} kept processing the other messages, at least {lowest.Value:G3} a second, while one kept failing.",
                        "supports",
                        0.5,
                        [after[0].Id, lowest.Id],
                        after[0].At,
                        after[^1].At,
                        "call_queue_processed_rate",
                        $"queue-processed:{receives.Service}");
                }

                ContradictChangesAfter(output, changes, receives.Service, first.At, $"{receives.Service}'s receive count was already climbing at {first.At:O}", "call_queue_max_receive_count", first.Id, "poison-before-change");
            }

            foreach ((string callId, JsonNode result) in conversation.ToolResults.Where(entry => entry.Value.Tool == "log_signatures" && entry.Value.Result is not null).Select(entry => (entry.Key, entry.Value.Result!)))
            {
                string service = result["service"]?.GetValue<string>() ?? conversation.AffectedService;
                foreach (JsonNode signature in result["signatures"]?.AsArray().OfType<JsonNode>() ?? [])
                {
                    if (FailedMessage().Match(signature["message"]?.GetValue<string>() ?? string.Empty) is not { Success: true } failed)
                    {
                        continue;
                    }

                    string id = signature["id"]!.GetValue<string>();
                    int count = int.TryParse(signature["count"]?.GetValue<string>(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) ? parsed : 1;
                    if (count < PoisonReceives)
                    {
                        continue;
                    }

                    DateTimeOffset from = Time(signature["firstSeen"]) ?? DateTimeOffset.UtcNow;
                    DateTimeOffset to = Time(signature["lastSeen"]) ?? from;
                    if (to < onset - OngoingAfter)
                    {
                        continue;
                    }

                    string hypothesis = PoisonHypothesis(output, service, conversation.AffectedService);
                    output.Evidence(hypothesis, $"{service} logged {count} failures of message {failed.Groups[1].Value} from {from:O} to {to:O}.", "supports", 0.7, [id], from, to, callId, $"logs:{service}:poison-message");
                    ContradictChangesAfter(output, changes, service, from, $"Message {failed.Groups[1].Value} was already failing at {from:O} and kept failing until {to:O}", callId, id, "poison-before-change");
                }
            }
        }

        /// <summary>
        /// R10 (Phase E01): orders booked again while no message is received twice. The duplicates are separate
        /// publications, not redeliveries, meeting processing that is not idempotent; a change that began serving after
        /// the first duplicate cannot explain them.
        /// </summary>
        private static void DuplicateDeliveries(Conversation conversation, Findings output, Change[] changes, DateTimeOffset onset)
        {
            foreach (Series duplicates in SeriesOf(conversation, "queue_duplicate_rate"))
            {
                if (Ongoing(duplicates, onset, 0) is not { } samples || samples.MaxBy(sample => sample.Value) is not { Value: >= DuplicateRate } peak)
                {
                    continue;
                }

                Sample first = samples.First(sample => sample.Value > 0);
                string hypothesis = DuplicateHypothesis(output, duplicates.Service, conversation.AffectedService);
                output.Evidence(
                    hypothesis,
                    $"{duplicates.Service} began booking orders it had booked already at {first.At:O}, up to {peak.Value:G3} a second by {peak.At:O}.",
                    "supports",
                    0.8,
                    [first.Id, peak.Id],
                    first.At,
                    peak.At,
                    "call_queue_duplicate_rate",
                    $"queue-duplicates:{duplicates.Service}");

                if (SeriesOf(conversation, "queue_max_receive_count").FirstOrDefault(series => series.Service == duplicates.Service) is { Samples.Count: > 0 } receives
                    && receives.Samples.Where(sample => sample.At >= first.At && sample.At <= onset + OngoingAfter).ToArray() is { Length: > 0 } after
                    && after.Max(sample => sample.Value) <= 1)
                {
                    output.Evidence(
                        hypothesis,
                        $"No message on {duplicates.Service}'s queue was received more than once while the duplicates were booked, so they are separate publications, not redeliveries.",
                        "supports",
                        0.6,
                        [after[0].Id, after[^1].Id],
                        after[0].At,
                        after[^1].At,
                        "call_queue_max_receive_count",
                        $"queue-receives:{duplicates.Service}");
                }

                ContradictChangesAfter(output, changes, duplicates.Service, first.At, $"{duplicates.Service} was already booking orders twice at {first.At:O}", "call_queue_duplicate_rate", first.Id, "duplicates-before-change");
            }

            foreach ((string callId, JsonNode result) in conversation.ToolResults.Where(entry => entry.Value.Tool == "log_signatures" && entry.Value.Result is not null).Select(entry => (entry.Key, entry.Value.Result!)))
            {
                string service = result["service"]?.GetValue<string>() ?? conversation.AffectedService;
                foreach (JsonNode signature in result["signatures"]?.AsArray().OfType<JsonNode>() ?? [])
                {
                    if (!BookedAgain().IsMatch(signature["message"]?.GetValue<string>() ?? string.Empty))
                    {
                        continue;
                    }

                    string id = signature["id"]!.GetValue<string>();
                    int count = int.TryParse(signature["count"]?.GetValue<string>(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) ? parsed : 1;
                    DateTimeOffset from = Time(signature["firstSeen"]) ?? DateTimeOffset.UtcNow;
                    DateTimeOffset to = Time(signature["lastSeen"]) ?? from;
                    if (to < onset - OngoingAfter)
                    {
                        continue;
                    }

                    string hypothesis = DuplicateHypothesis(output, service, conversation.AffectedService);
                    output.Evidence(hypothesis, $"{service} logged {count} orders booked again from a second message, from {from:O} to {to:O}.", "supports", 0.7, [id], from, to, callId, $"logs:{service}:duplicate-delivery");
                    ContradictChangesAfter(output, changes, service, from, $"{service} was already booking orders twice at {from:O}", callId, id, "duplicates-before-change");
                }
            }
        }

        private static string DuplicateHypothesis(Findings output, string service, string affected) => output.Hypothesis(
            $"duplicate-delivery:{service}",
            $"Orders reach {service}'s queue twice, in separate messages, and its booking is not idempotent, so they are booked twice.",
            ["an order published twice", "each copy is received once", "the booking does not check for an earlier one", $"{affected} books the order twice"],
            service,
            null);

        private static string PoisonHypothesis(Findings output, string service, string affected) => output.Hypothesis(
            $"poison-message:{service}",
            $"A poison message on {service}'s queue fails on every receive while the other messages are processed.",
            ["a message no revision can process", "it fails and returns after its visibility timeout", "its receive count and the queue's oldest-message age rise", $"{affected} detects the ageing queue"],
            service,
            null);

        private static void ContradictChangesAfter(Findings output, Change[] changes, string service, DateTimeOffset began, string fact, string callId, string observationId, string family)
        {
            foreach (Change change in changes.Where(change => change.Service == service && change.Kind is "deploy" or "rollback" && change.ServingFrom is { } serving && serving > began))
            {
                string hypothesis = output.Hypothesis(change.Key, $"The {change.Service} {change.Version} deployment caused the symptoms.", [$"deploy {change.Service} {change.Version}", $"regression in {change.Service}", "symptoms"], change.Service, change.StartedId);
                output.Evidence(hypothesis, $"{fact}, before {change.Service} {change.Version} served from {change.ServingFrom:O}.", "contradicts", 0.8, [observationId, change.StartedId], began, change.ServingFrom!.Value, callId, $"{family}:{change.Service}");
            }
        }

        /// <summary>R8: slow calls to a dependency in traces.</summary>
        private static void SlowCalls(Conversation conversation, Findings output, Change[] changes)
        {
            foreach ((string callId, JsonNode result) in conversation.ToolResults.Where(entry => entry.Value.Tool == "slow_spans" && entry.Value.Result is not null).Select(entry => (entry.Key, entry.Value.Result!)))
            {
                IEnumerable<IGrouping<string, JsonNode>> slow = (result["spans"]?.AsArray().OfType<JsonNode>() ?? [])
                    .Where(span => span["calls"]?.GetValue<string>() is { Length: > 0 } && span["durationMs"]?.GetValue<double>() >= 500)
                    .GroupBy(span => span["calls"]!.GetValue<string>());
                foreach (IGrouping<string, JsonNode> calls in slow)
                {
                    JsonNode[] spans = [.. calls.OrderBy(span => span["t"]!.GetValue<string>(), StringComparer.Ordinal)];
                    DateTimeOffset first = Time(spans[0]["t"]) ?? DateTimeOffset.UtcNow;
                    DateTimeOffset last = Time(spans[^1]["t"]) ?? first;
                    Change? cause = changes.FirstOrDefault(change => change.Service == calls.Key && change.Kind is "deploy" or "rollback" && change.ServingFrom is { } serving && serving <= first);
                    string hypothesis = cause is not null
                        ? output.Hypothesis(cause.Key, $"The {cause.Service} {cause.Version} deployment slowed {cause.Service}.", [$"deploy {cause.Service} {cause.Version}", $"{cause.Service} slow calls", "SLO burn"], cause.Service, cause.StartedId)
                        : output.Hypothesis($"dependency-regression:{calls.Key}", $"{calls.Key} slowed down, and {conversation.AffectedService} timed out waiting for it.", [$"{calls.Key} slow calls", $"{conversation.AffectedService} timeouts", "SLO burn"], calls.Key, null);
                    double longest = spans.Max(span => span["durationMs"]!.GetValue<double>());
                    output.Evidence(hypothesis, $"{spans.Length} of {conversation.AffectedService}'s slowest calls went to {calls.Key}, up to {longest:F0} ms, from {first:O} to {last:O}.", "supports", 0.6, [.. spans.Select(span => span["id"]!.GetValue<string>())], first, last, callId, $"traces:{conversation.AffectedService}->{calls.Key}");
                }
            }
        }

        /// <summary>R9: observed dependencies are context for the causal graph, not support for any one cause.</summary>
        private static void Topology(Conversation conversation, Findings output)
        {
            foreach ((string callId, JsonNode result) in conversation.ToolResults.Where(entry => entry.Value.Tool == "service_dependencies" && entry.Value.Result is not null).Select(entry => (entry.Key, entry.Value.Result!)))
            {
                JsonNode[] edges = [.. (result["edges"]?.AsArray().OfType<JsonNode>() ?? []).Where(edge => edge["from"]?.GetValue<string>() == conversation.AffectedService || edge["to"]?.GetValue<string>() == conversation.AffectedService)];
                if (edges.Length == 0)
                {
                    output.Unknown($"No dependency edges of {conversation.AffectedService} were observed in traces.");
                    continue;
                }

                DateTimeOffset now = conversation.SymptomOnset ?? DateTimeOffset.UtcNow;
                output.Evidence(
                    null,
                    $"{conversation.AffectedService} is connected to {string.Join(", ", edges.Select(edge => edge["from"]!.GetValue<string>() == conversation.AffectedService ? $"{edge["to"]} (calls it)" : $"{edge["from"]} (called by it)"))}.",
                    "neutral",
                    0.3,
                    [.. edges.Select(edge => edge["id"]!.GetValue<string>())],
                    now,
                    now,
                    callId,
                    $"topology:{conversation.AffectedService}");
            }
        }

        /// <summary>
        /// The samples around <paramref name="onset"/> of an excursion still under way then, or null when the last of them is
        /// back at <paramref name="floor"/>: duplicates, retries or redeliveries that ended before the symptoms began, such
        /// as an earlier scenario's still inside the window, cannot explain them.
        /// </summary>
        private static Sample[]? Ongoing(Series series, DateTimeOffset onset, double floor)
        {
            Sample[] samples = [.. series.Samples.Where(sample => sample.At >= onset - OngoingBefore && sample.At <= onset + OngoingAfter)];
            return samples is [.., { } last] && last.Value > floor ? samples : null;
        }

        /// <summary>The start of the last run of burning samples: the current excursion, not an earlier one still inside the window.</summary>
        private static DateTimeOffset? Onset(Series? burn)
        {
            if (burn is not { Samples.Count: > 0 })
            {
                return null;
            }

            int last = burn.Samples.Count - 1;
            while (last >= 0 && burn.Samples[last].Value <= 1.0)
            {
                last--;
            }

            if (last < 0)
            {
                return burn.Samples[^1].At;
            }

            int first = last;
            while (first > 0 && burn.Samples[first - 1].Value > 1.0)
            {
                first--;
            }

            return burn.Samples[first].At;
        }

        private static Regex FailedMessage() => new(@"message (\S+) failed on receive", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));

        private static Regex BookedAgain() => new(@"was booked again from message", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));

        private static Regex GaveUp() => new("gave up on ([a-z0-9-]+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));

        private static DateTimeOffset? Time(JsonNode? node) =>
            node?.GetValue<string>() is { } text && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTimeOffset parsed) ? parsed : null;

        private static Series[] SeriesOf(Conversation conversation, string metric) =>
            conversation.ToolResults.Values
                .Where(result => result.Tool == "metric_range" && result.Result?["metric"]?.GetValue<string>() == metric)
                .SelectMany(result => result.Result!["series"]?.AsArray() ?? [])
                .Select(series => new Series(
                    series!["service"]!.GetValue<string>(),
                    [.. series["samples"]!.AsArray().Select(sample => new Sample(
                        sample!["id"]!.GetValue<string>(),
                        DateTimeOffset.Parse(sample["t"]!.GetValue<string>(), CultureInfo.InvariantCulture),
                        sample["v"]!.GetValue<double>()))]))
                .ToArray();

        private static Change[] Changes(Conversation conversation) =>
            conversation.ToolResults.Values
                .Where(result => result.Tool == "recent_deployments")
                .SelectMany(result => result.Result?["changes"]?.AsArray() ?? [])
                .GroupBy(change => change!["deploymentId"]!.GetValue<string>())
                .Select(group =>
                {
                    JsonNode first = group.First()!;
                    JsonNode? completed = group.LastOrDefault(change => change!["servingFrom"] is not null);
                    return new Change(
                        group.Key,
                        first["service"]!.GetValue<string>(),
                        first["version"]!.GetValue<string>(),
                        first["kind"]!.GetValue<string>(),
                        DateTimeOffset.Parse(first["startedAt"]!.GetValue<string>(), CultureInfo.InvariantCulture),
                        completed?["servingFrom"] is { } serving ? DateTimeOffset.Parse(serving.GetValue<string>(), CultureInfo.InvariantCulture) : null,
                        [.. group.Select(change => change!["id"]!.GetValue<string>())]);
                })
                .OrderBy(change => change.StartedAt)
                .ToArray();

        internal sealed record Sample(string Id, DateTimeOffset At, double Value);

        internal sealed record Series(string Service, IReadOnlyList<Sample> Samples);

        internal sealed record Change(string DeploymentId, string Service, string Version, string Kind, DateTimeOffset StartedAt, DateTimeOffset? ServingFrom, IReadOnlyList<string> ObservationIds)
        {
            public string StartedId => ObservationIds[0];

            public string Key => $"change:{StartedId}";
        }

        internal sealed record Surge(Series Series, double Baseline, Sample Start, Sample Peak)
        {
            /// <summary>A material rise in connections that began before symptoms.</summary>
            public static Surge? Of(Series series, DateTimeOffset onset) =>
                Shift.Of(series, onset, minimumRise: 30, minimumRatio: 2) is { } shift ? new Surge(series, shift.Baseline, shift.Start, shift.Peak) : null;
        }

        internal sealed record Regression(Series Series, double Baseline, Sample Start, Sample Peak)
        {
            /// <summary>A material latency rise, at least 0.2 s and three times the baseline, that began before symptoms.</summary>
            public static Regression? Of(Series series, DateTimeOffset onset) =>
                Shift.Of(series, onset, minimumRise: 0.2, minimumRatio: 3) is { } shift ? new Regression(series, shift.Baseline, shift.Start, shift.Peak) : null;
        }

        private sealed record Shift(double Baseline, Sample Start, Sample Peak)
        {
            public static Shift? Of(Series series, DateTimeOffset onset, double minimumRise, double minimumRatio)
            {
                if (series.Samples.Count < 4)
                {
                    return null;
                }

                Sample[] early = [.. series.Samples.Take(Math.Max(1, series.Samples.Count * 3 / 10))];
                double baseline = early.Min(sample => sample.Value);
                Sample[] considered = [.. series.Samples.Where(sample => sample.At <= onset + TimeSpan.FromSeconds(30))];
                if (considered.Length == 0)
                {
                    return null;
                }

                Sample peak = considered.MaxBy(sample => sample.Value)!;
                double rise = peak.Value - baseline;
                if (rise < minimumRise || peak.Value < minimumRatio * Math.Max(baseline, minimumRise / 10))
                {
                    return null;
                }

                Sample start = considered.First(sample => sample.Value >= baseline + rise * 0.2);

                // A shift that had already subsided by onset cannot explain symptoms at onset.
                Sample atOnset = considered.Where(sample => sample.At <= onset + TimeSpan.FromSeconds(10)).MaxBy(sample => sample.At) ?? peak;
                return start.At < onset && atOnset.Value >= baseline + rise * 0.5 ? new Shift(baseline, start, peak) : null;
            }
        }

        private sealed class Findings
        {
            private readonly List<JsonObject> _hypotheses = [];
            private readonly List<JsonObject> _evidence = [];
            private readonly List<string> _unknowns = [];

            public int HypothesisCount => _hypotheses.Count;

            public string FirstHypothesis => _hypotheses[0]["id"]!.GetValue<string>();

            /// <summary>The hypothesis with this key, created on first use; <paramref name="reword"/> lets a sharper explanation replace the statement.</summary>
            public string Hypothesis(string key, string statement, string[] chain, string? service, string? change, bool reword = false)
            {
                if (_hypotheses.FirstOrDefault(item => item["key"]!.GetValue<string>() == key) is { } existing)
                {
                    if (reword)
                    {
                        existing["statement"] = statement;
                        existing["causalChain"] = new JsonArray([.. chain.Select(step => (JsonNode)step)]);
                    }

                    return existing["id"]!.GetValue<string>();
                }

                string id = $"h{_hypotheses.Count + 1}";
                _hypotheses.Add(new JsonObject
                {
                    ["id"] = id,
                    ["key"] = key,
                    ["statement"] = statement,
                    ["causalChain"] = new JsonArray([.. chain.Select(step => (JsonNode)step)]),
                    ["suspectedService"] = service,
                    ["suspectedChangeObservationId"] = change,
                    ["supportingEvidenceIds"] = new JsonArray(),
                    ["contradictingEvidenceIds"] = new JsonArray()
                });
                return id;
            }

            public void Evidence(string? hypothesis, string statement, string direction, double strength, IReadOnlyList<string> observations, DateTimeOffset from, DateTimeOffset to, string toolCallId, string? group)
            {
                string id = $"e{_evidence.Count + 1}";
                _evidence.Add(new JsonObject
                {
                    ["id"] = id,
                    ["statement"] = statement,
                    ["direction"] = direction,
                    ["strength"] = strength,
                    ["observationIds"] = new JsonArray([.. observations.Distinct().Select(observation => (JsonNode)observation)]),
                    ["hypothesisIds"] = hypothesis is null ? new JsonArray() : new JsonArray(hypothesis),
                    ["from"] = from.ToString("O", CultureInfo.InvariantCulture),
                    ["to"] = (to < from ? from : to).ToString("O", CultureInfo.InvariantCulture),
                    ["toolCallId"] = toolCallId,
                    ["independenceGroup"] = group
                });

                if (hypothesis is not null && direction != "neutral")
                {
                    JsonObject target = _hypotheses.First(item => item["id"]!.GetValue<string>() == hypothesis);
                    target[direction == "contradicts" ? "contradictingEvidenceIds" : "supportingEvidenceIds"]!.AsArray().Add(id);
                }
            }

            public void Unknown(string text) => _unknowns.Add(text);

            public string ToJson(bool singleHypothesis) => new JsonObject
            {
                ["abstain"] = false,
                ["evidence"] = new JsonArray([.. _evidence.Select(item => item.DeepClone())]),
                ["hypotheses"] = new JsonArray([.. _hypotheses.Take(singleHypothesis ? 1 : _hypotheses.Count).Select(item => item.DeepClone())]),
                ["unknowns"] = new JsonArray([.. _unknowns.Select(text => (JsonNode)text)])
            }.ToJsonString();
        }
    }
}
