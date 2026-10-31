using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Sentinel.AgentWorker.Application.Tools;
using Sentinel.Contracts;
using Sentinel.Contracts.Investigations;
using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.AgentWorker.Application.Investigations;

/// <summary>
/// Validates the escalation investigator's proposal and measures its outcome (Phase E05), so the model only proposes the
/// experiment: the query must name the two explanations it was given, a catalogue metric and a range inside the incident's
/// window, with one prediction each; the result decides which prediction held, by threshold, at symptom onset.
/// </summary>
public static class DiscriminatingQueries
{
    private const double Strength = 0.7;

    // Keys a conclusion would carry; a discriminating query only observes.
    private static readonly string[] RefusedKeys = ["actions", "diagnosis", "confidence", "decision"];

    // A sample is read at onset: the last one up to this long after it.
    private static readonly TimeSpan OnsetTolerance = TimeSpan.FromSeconds(10);

    /// <summary>The proposal, or the reasons it is refused.</summary>
    public static (DiscriminatingQuery? Query, IReadOnlyList<string> Errors) Parse(string content, InvestigateIncident command, IReadOnlyDictionary<string, string> metrics)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(metrics);

        var errors = new List<string>();
        JsonObject? root;
        try
        {
            root = JsonNode.Parse(content) as JsonObject;
        }
        catch (JsonException exception)
        {
            return (null, [$"The answer is not JSON: {exception.Message}"]);
        }

        if (root is null)
        {
            return (null, ["The answer is not a JSON object."]);
        }

        foreach (string refused in RefusedKeys.Where(root.ContainsKey))
        {
            errors.Add($"'{refused}' is not part of a discriminating query; Sentinel decides, the query only observes.");
        }

        HashSet<string> given = [.. (command.Hypotheses ?? []).Select(hypothesis => hypothesis.HypothesisId.ToString())];
        string[] separated = [.. (root["hypothesisIds"] as JsonArray ?? []).Select(node => node?.GetValue<string>() ?? string.Empty)];
        if (separated.Length != 2 || separated.Distinct(StringComparer.Ordinal).Count() != 2 || !separated.All(given.Contains))
        {
            errors.Add("hypothesisIds must name the two explanations the task gives, once each.");
        }

        string tool = root["tool"]?.GetValue<string>() ?? string.Empty;
        if (tool != MetricRangeTool.Name)
        {
            errors.Add($"Tool '{tool}' is not available to the escalation investigator. Available: {MetricRangeTool.Name}.");
        }

        JsonObject arguments = root["arguments"] as JsonObject ?? [];
        string metric = arguments["metric"]?.GetValue<string>() ?? string.Empty;
        if (!metrics.ContainsKey(metric))
        {
            errors.Add($"Metric '{metric}' is not in the catalogue.");
        }

        foreach (string bound in new[] { "from", "to" })
        {
            if (arguments[bound]?.GetValue<string>() is { } text
                && (!DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTimeOffset at)
                    || at < command.ObservationWindow.From || at > command.ObservationWindow.To))
            {
                errors.Add($"'{bound}' must lie inside the incident's observation window, {command.ObservationWindow.From:O} to {command.ObservationWindow.To:O}.");
            }
        }

        var expectations = new List<QueryExpectation>();
        foreach (JsonObject expectation in (root["expectations"] as JsonArray ?? []).OfType<JsonObject>())
        {
            string? hypothesis = expectation["hypothesisId"]?.GetValue<string>();
            string? service = expectation["service"]?.GetValue<string>();
            string? comparison = expectation["comparison"]?.GetValue<string>();
            double? threshold = expectation["threshold"]?.GetValue<double>();
            if (hypothesis is null || !separated.Contains(hypothesis) || string.IsNullOrWhiteSpace(service) || comparison is not ("above" or "below") || threshold is not { } value || !double.IsFinite(value)
                || expectation["metric"]?.GetValue<string>() != metric)
            {
                errors.Add("Each expectation names one of the two hypotheses, a service, the queried metric, above or below, and a finite threshold.");
                continue;
            }

            expectations.Add(new QueryExpectation
            {
                HypothesisId = HypothesisId.From(Guid.Parse(hypothesis)),
                ServiceId = ServiceId.From(service),
                Metric = metric,
                Comparison = comparison,
                Threshold = value,
                Statement = expectation["statement"]?.GetValue<string>() ?? $"{service} {metric} {comparison} {value}"
            });
        }

        if (expectations.Select(expectation => expectation.HypothesisId).Distinct().Count() != 2)
        {
            errors.Add("Give exactly one expectation for each of the two hypotheses.");
        }

        if (errors.Count > 0)
        {
            return (null, errors);
        }

        return (new DiscriminatingQuery
        {
            HypothesisIds = [.. separated.Select(id => HypothesisId.From(Guid.Parse(id)))],
            Tool = tool,
            Arguments = arguments.ToJsonString(),
            Expectations = expectations,
            Rationale = root["rationale"]?.GetValue<string>() ?? string.Empty
        }, []);
    }

    /// <summary>
    /// The evidence the query's result gives: when exactly one prediction held, it supports that explanation and
    /// contradicts the other; otherwise one neutral finding, and the unknown it leaves.
    /// </summary>
    public static (IReadOnlyList<EvidenceFinding> Evidence, string? Unknown) Measure(DiscriminatingQuery query, string toolContent, string toolCallId, DateTimeOffset onset)
    {
        ArgumentNullException.ThrowIfNull(query);

        var content = JsonNode.Parse(toolContent);
        var readings = new List<(QueryExpectation Expectation, string Id, DateTimeOffset At, double Value, bool Held)>();
        foreach (QueryExpectation expectation in query.Expectations)
        {
            JsonObject? series = (content?["series"] as JsonArray ?? []).OfType<JsonObject>().FirstOrDefault(item => item["service"]?.GetValue<string>() == expectation.ServiceId.Value);
            JsonObject[] samples = [.. (series?["samples"] as JsonArray ?? []).OfType<JsonObject>()];
            JsonObject? sample = samples.LastOrDefault(item => Time(item) <= onset + OnsetTolerance) ?? samples.FirstOrDefault();
            if (sample is null)
            {
                return ([], $"The discriminating query returned no {expectation.Metric} for {expectation.ServiceId}, so it separated nothing.");
            }

            double value = sample["v"]!.GetValue<double>();
            bool held = expectation.Comparison == "above" ? value > expectation.Threshold : value < expectation.Threshold;
            readings.Add((expectation, sample["id"]!.GetValue<string>(), Time(sample), value, held));
        }

        ObservationId[] cited = [.. readings.Select(reading => ObservationId.From(reading.Id)).Distinct()];
        var range = new TimeRange(readings.Min(reading => reading.At), readings.Max(reading => reading.At));
        string group = $"escalation:{query.Expectations[0].Metric}";
        string observed = string.Join("; ", readings.Select(reading => string.Create(CultureInfo.InvariantCulture, $"{reading.Expectation.ServiceId} {reading.Expectation.Metric} was {reading.Value:G4} at {reading.At:O}")));
        if (readings.Count(reading => reading.Held) != 1)
        {
            var neutral = new EvidenceFinding
            {
                EvidenceId = EvidenceId.New(),
                Statement = $"The discriminating query did not separate the explanations: {observed}.",
                Direction = EvidenceDirection.Neutral,
                Strength = 0.3,
                SourceObservationIds = cited,
                HypothesisIds = [.. query.HypothesisIds],
                EventTimeRange = range,
                ToolCallId = toolCallId,
                IndependenceGroup = group
            };
            return ([neutral], "The one observation the escalation proposed matched both predictions or neither.");
        }

        return ([.. readings.Select(reading => new EvidenceFinding
        {
            EvidenceId = EvidenceId.New(),
            Statement = $"{observed}: the prediction \"{reading.Expectation.Statement}\" {(reading.Held ? "held" : "failed")}.",
            Direction = reading.Held ? EvidenceDirection.Supports : EvidenceDirection.Contradicts,
            Strength = Strength,
            SourceObservationIds = cited,
            HypothesisIds = [reading.Expectation.HypothesisId],
            EventTimeRange = range,
            ToolCallId = toolCallId,
            IndependenceGroup = group
        })], null);
    }

    private static DateTimeOffset Time(JsonObject sample) => DateTimeOffset.Parse(sample["t"]!.GetValue<string>(), CultureInfo.InvariantCulture);
}
