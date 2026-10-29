using System.Globalization;
using System.Text.Json;
using Sentinel.Contracts;
using Sentinel.Contracts.Investigations;
using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.AgentWorker.Application.Investigations;

/// <summary>
/// Validates the model's structured output (ADR-015). Model output is an untrusted proposal: every
/// field is checked, and every cited observation must have been returned by a tool call in this
/// execution, so a fabricated or remembered ID is refused. Invalid output is never coerced.
/// </summary>
public static class FindingsValidator
{
    public const string OutputSchemaName = "sentinel.investigation-findings.v1";
    public const int MinimumHypotheses = 2;
    public const int MaxUnknowns = 10;

    /// <summary>
    /// Properties a findings document may not carry. Free text cannot name an executable capability:
    /// only Runtime's validated, typed plans can (Phase 04 safety rule), so a model persuaded by
    /// injected text to "run" something is refused, not obeyed.
    /// </summary>
    public static readonly IReadOnlyList<string> ForbiddenProperties = ["actions", "action", "commands", "command", "execute", "remediation", "plan", "plans", "tools"];

    private static readonly System.Text.RegularExpressions.Regex KeyPattern = new("^(change:\\S+|[a-z][a-z0-9-]*(:[a-z0-9][a-z0-9.-]*)?)$", System.Text.RegularExpressions.RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));

    /// <summary>The output schema, shown to the model in the system prompt.</summary>
    public const string OutputSchema = """
        {
          "abstain": false,
          "abstentionReason": null,
          "evidence": [{
            "id": "e1",
            "statement": "what the data shows, in one sentence",
            "direction": "supports | contradicts | neutral",
            "strength": 0.0,
            "observationIds": ["IDs exactly as returned by tools"],
            "hypothesisIds": ["h1"],
            "from": "ISO-8601 start of the signal", "to": "ISO-8601 end of the signal",
            "toolCallId": "the tool call that returned the observations",
            "independenceGroup": "the underlying signal, so copies of it are counted once"
          }],
          "hypotheses": [{
            "id": "h1",
            "key": "change:<deployment observation ID> | redis-saturation[:service] | dependency-regression[:service] | load-change:service | external-dependency",
            "statement": "a possible cause, in one sentence",
            "causalChain": ["cause", "effect", "symptom"],
            "suspectedService": "service name or null",
            "suspectedChangeObservationId": "the deployment observation ID or null",
            "supportingEvidenceIds": ["e1"],
            "contradictingEvidenceIds": []
          }],
          "unknowns": ["what could not be determined, and what would decide it"]
        }
        """;

    public static ValidatedFindings Validate(string? output, IReadOnlySet<ObservationId> returnedObservations, IReadOnlySet<string> toolCallIds) =>
        Validate(output, returnedObservations, toolCallIds, MinimumHypotheses);

    public static ValidatedFindings Validate(string? output, IReadOnlySet<ObservationId> returnedObservations, IReadOnlySet<string> toolCallIds, int minimumHypotheses)
    {
        ArgumentNullException.ThrowIfNull(returnedObservations);
        ArgumentNullException.ThrowIfNull(toolCallIds);

        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(output))
        {
            return Refused("The output is empty.");
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(output);
        }
        catch (JsonException exception)
        {
            return Refused($"The output is not JSON: {exception.Message}");
        }

        using (document)
        {
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return Refused("The output must be a JSON object.");
            }

            foreach (JsonProperty property in root.EnumerateObject().Where(property => ForbiddenProperties.Contains(property.Name.ToLowerInvariant())))
            {
                errors.Add($"Findings may not contain '{property.Name}': investigators propose explanations, never actions.");
            }

            if (errors.Count > 0)
            {
                return new ValidatedFindings([], [], false, null, errors);
            }

            if (root.TryGetProperty("abstain", out JsonElement abstain) && abstain.ValueKind == JsonValueKind.True)
            {
                string reason = Text(root, "abstentionReason") ?? "The model abstained without a reason.";
                return new ValidatedFindings([], [], true, reason, []);
            }

            Dictionary<string, HypothesisId> hypothesisIds = MapIds(root, "hypotheses", errors, _ => HypothesisId.New());
            Dictionary<string, EvidenceId> evidenceIds = MapIds(root, "evidence", errors, _ => EvidenceId.New());

            var evidence = new List<EvidenceFinding>();
            foreach (JsonElement item in Array(root, "evidence"))
            {
                string label = Text(item, "id") ?? "?";
                if (ParseEvidence(item, label, evidenceIds, hypothesisIds, returnedObservations, toolCallIds, errors) is { } finding)
                {
                    evidence.Add(finding);
                }
            }

            var hypotheses = new List<HypothesisFinding>();
            foreach (JsonElement item in Array(root, "hypotheses"))
            {
                string label = Text(item, "id") ?? "?";
                if (ParseHypothesis(item, label, evidenceIds, hypothesisIds, returnedObservations, errors) is { } finding)
                {
                    hypotheses.Add(finding);
                }
            }

            if (hypotheses.Count < minimumHypotheses)
            {
                errors.Add($"At least {minimumHypotheses} competing hypotheses are required; {hypotheses.Count} were valid.");
            }

            string[] unknowns = [.. Strings(root, "unknowns").Where(text => !string.IsNullOrWhiteSpace(text)).Take(MaxUnknowns).Select(text => text.Length > 300 ? text[..300] : text)];
            return new ValidatedFindings(evidence, hypotheses, false, null, errors, unknowns);
        }

        static ValidatedFindings Refused(string error) => new([], [], false, null, [error]);
    }

    private static EvidenceFinding? ParseEvidence(
        JsonElement item,
        string label,
        Dictionary<string, EvidenceId> evidenceIds,
        Dictionary<string, HypothesisId> hypothesisIds,
        IReadOnlySet<ObservationId> returned,
        IReadOnlySet<string> toolCallIds,
        List<string> errors)
    {
        int before = errors.Count;
        string? statement = Text(item, "statement");
        if (string.IsNullOrWhiteSpace(statement))
        {
            errors.Add($"Evidence {label} has no statement.");
        }

        EvidenceDirection? direction = Text(item, "direction") switch
        {
            "supports" => EvidenceDirection.Supports,
            "contradicts" => EvidenceDirection.Contradicts,
            "neutral" => EvidenceDirection.Neutral,
            _ => null
        };
        if (direction is null)
        {
            errors.Add($"Evidence {label} needs a direction of supports, contradicts or neutral.");
        }

        double strength = item.TryGetProperty("strength", out JsonElement value) && value.TryGetDouble(out double parsed) ? parsed : -1;
        if (strength is < 0 or > 1)
        {
            errors.Add($"Evidence {label} needs a strength from 0 to 1.");
        }

        ObservationId[] observations = [.. Strings(item, "observationIds").Select(TryObservation).OfType<ObservationId>()];
        if (observations.Length == 0)
        {
            errors.Add($"Evidence {label} cites no observation.");
        }

        foreach (ObservationId observation in observations.Where(id => !returned.Contains(id)))
        {
            errors.Add($"Evidence {label} cites {observation}, which no tool returned in this investigation.");
        }

        string? toolCallId = Text(item, "toolCallId");
        if (toolCallId is null || !toolCallIds.Contains(toolCallId))
        {
            errors.Add($"Evidence {label} must name the tool call that produced it.");
        }

        HypothesisId[] linked = ResolveAll(Strings(item, "hypothesisIds"), hypothesisIds, $"Evidence {label}", errors);
        DateTimeOffset? from = Time(item, "from");
        DateTimeOffset? to = Time(item, "to");
        if (from is null || to is null || to < from)
        {
            errors.Add($"Evidence {label} needs an event-time range with from no later than to.");
        }

        return errors.Count > before
            ? null
            : new EvidenceFinding
            {
                EvidenceId = evidenceIds[label],
                Statement = statement!,
                Direction = direction!.Value,
                Strength = strength,
                SourceObservationIds = observations,
                HypothesisIds = linked,
                EventTimeRange = new TimeRange(from!.Value, to!.Value),
                ToolCallId = toolCallId!,
                IndependenceGroup = Text(item, "independenceGroup")
            };
    }

    private static HypothesisFinding? ParseHypothesis(
        JsonElement item,
        string label,
        Dictionary<string, EvidenceId> evidenceIds,
        Dictionary<string, HypothesisId> hypothesisIds,
        IReadOnlySet<ObservationId> returned,
        List<string> errors)
    {
        int before = errors.Count;
        string? statement = Text(item, "statement");
        if (string.IsNullOrWhiteSpace(statement))
        {
            errors.Add($"Hypothesis {label} has no statement.");
        }

        string[] chain = [.. Strings(item, "causalChain")];
        if (chain.Length == 0)
        {
            errors.Add($"Hypothesis {label} needs a causal chain.");
        }

        ServiceId? service = null;
        if (Text(item, "suspectedService") is { } serviceName)
        {
            try
            {
                service = ServiceId.From(serviceName);
            }
            catch (ArgumentException)
            {
                errors.Add($"Hypothesis {label} names '{serviceName}', which is not a service name.");
            }
        }

        ObservationId? change = Text(item, "suspectedChangeObservationId") is { } changeText ? TryObservation(changeText) : null;
        if (change is { } changeId && !returned.Contains(changeId))
        {
            errors.Add($"Hypothesis {label} suspects change {changeId}, which no tool returned.");
        }

        string? key = Text(item, "key");
        if (key is not null && !KeyPattern.IsMatch(key))
        {
            errors.Add($"Hypothesis {label} has key '{key}', which is not in the key vocabulary.");
        }

        EvidenceId[] supporting = ResolveAll(Strings(item, "supportingEvidenceIds"), evidenceIds, $"Hypothesis {label}", errors);
        EvidenceId[] contradicting = ResolveAll(Strings(item, "contradictingEvidenceIds"), evidenceIds, $"Hypothesis {label}", errors);

        return errors.Count > before || !hypothesisIds.TryGetValue(label, out HypothesisId hypothesisId)
            ? null
            : new HypothesisFinding
            {
                HypothesisId = hypothesisId,
                Statement = statement!,
                CausalChain = chain,
                SuspectedServiceId = service,
                SuspectedChangeObservationId = change,
                SupportingEvidenceIds = supporting,
                ContradictingEvidenceIds = contradicting,
                Key = key
            };
    }

    private static Dictionary<string, TId> MapIds<TId>(JsonElement root, string property, List<string> errors, Func<string, TId> create)
    {
        var map = new Dictionary<string, TId>(StringComparer.Ordinal);
        foreach (JsonElement item in Array(root, property))
        {
            string? label = Text(item, "id");
            if (label is null || !map.TryAdd(label, create(label)))
            {
                errors.Add($"Every item in {property} needs a unique id; '{label}' is missing or repeated.");
            }
        }

        return map;
    }

    private static TId[] ResolveAll<TId>(IEnumerable<string> labels, Dictionary<string, TId> map, string owner, List<string> errors)
    {
        var resolved = new List<TId>();
        foreach (string label in labels)
        {
            if (map.TryGetValue(label, out TId? id))
            {
                resolved.Add(id);
            }
            else
            {
                errors.Add($"{owner} refers to '{label}', which is not defined.");
            }
        }

        return [.. resolved];
    }

    private static ObservationId? TryObservation(string value)
    {
        try
        {
            return ObservationId.From(value);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static JsonElement[] Array(JsonElement element, string property) =>
        element.TryGetProperty(property, out JsonElement value) && value.ValueKind == JsonValueKind.Array ? [.. value.EnumerateArray()] : [];

    private static IEnumerable<string> Strings(JsonElement element, string property) =>
        Array(element, property).Where(item => item.ValueKind == JsonValueKind.String).Select(item => item.GetString()!);

    private static string? Text(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static DateTimeOffset? Time(JsonElement element, string property) =>
        Text(element, property) is { } text && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTimeOffset parsed)
            ? parsed
            : null;
}
