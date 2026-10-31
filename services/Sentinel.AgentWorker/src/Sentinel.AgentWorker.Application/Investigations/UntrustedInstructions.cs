using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Sentinel.AgentWorker.Domain.Executions;
using Sentinel.Contracts.Investigations;
using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.AgentWorker.Application.Investigations;

/// <summary>
/// Finds text in an execution's untrusted tool results, log lines and runbook passages, that reads as an instruction to an
/// AI agent, so that people see the attempt on the incident. The finding changes nothing: tool results are data, an
/// investigator's output that acts on them is refused by findings validation, and nothing found here is evidence. The
/// patterns are fixed and deliberately narrow, addressing an AI or overriding its instructions, so ordinary log lines
/// about rollbacks or restarts never match; an attempt they miss is still only data.
/// </summary>
public static partial class UntrustedInstructions
{
    private const int ExcerptLength = 240;

    private static readonly (string Name, Regex Pattern)[] Patterns =
    [
        ("ignore-instructions", IgnoreInstructions()),
        ("addressed-to-ai", AddressedToAi()),
        ("role-override", RoleOverride()),
        ("disregard-rules", DisregardRules())
    ];

    /// <summary>Every suspected injection in <paramref name="toolCalls"/>, once per source and pattern.</summary>
    public static IReadOnlyList<SuspectedInjection> Scan(IEnumerable<ToolCallRecord> toolCalls)
    {
        ArgumentNullException.ThrowIfNull(toolCalls);

        var found = new Dictionary<(string Source, string Pattern), SuspectedInjection>();
        foreach (ToolCallRecord call in toolCalls.Where(call => call.Error is null))
        {
            if (Parse(call.Result) is not JsonObject { } result || result["untrustedData"]?.GetValueKind() != JsonValueKind.True)
            {
                continue;
            }

            string? service = Text(result["service"]);
            foreach (JsonObject item in result.Select(property => property.Value).OfType<JsonArray>().SelectMany(array => array.OfType<JsonObject>()))
            {
                string? source = Text(item["id"]) ?? Text(item["document"]);
                if (source is null)
                {
                    continue;
                }

                foreach (string text in item.Select(property => Text(property.Value)).OfType<string>())
                {
                    foreach ((string name, Regex pattern) in Patterns)
                    {
                        if (pattern.Match(text) is { Success: true } match && !found.ContainsKey((source, name)))
                        {
                            found[(source, name)] = new SuspectedInjection
                            {
                                Tool = call.ToolName,
                                Source = source,
                                ServiceId = ServiceOf(Text(item["service"]) ?? service),
                                Excerpt = Excerpt(text, match.Index),
                                Pattern = name
                            };
                        }
                    }
                }
            }
        }

        return [.. found.Values];
    }

    private static JsonNode? Parse(string content)
    {
        try
        {
            return JsonNode.Parse(content);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? Text(JsonNode? node) => node is JsonValue value && value.TryGetValue(out string? text) ? text : null;

    private static ServiceId? ServiceOf(string? value)
    {
        try
        {
            return string.IsNullOrWhiteSpace(value) ? null : ServiceId.From(value);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    // The text around the match, so the excerpt shows the instruction even in a long line.
    private static string Excerpt(string text, int index)
    {
        if (text.Length <= ExcerptLength)
        {
            return text;
        }

        int start = Math.Clamp(index - ExcerptLength / 4, 0, text.Length - ExcerptLength);
        return string.Concat(start > 0 ? "…" : string.Empty, text.AsSpan(start, ExcerptLength), start + ExcerptLength < text.Length ? "…" : string.Empty);
    }

    [GeneratedRegex(@"\b(ignore|forget)\s+(all\s+|any\s+|your\s+|the\s+)?(previous|prior|earlier|above)\s+instructions\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 100)]
    private static partial Regex IgnoreInstructions();

    [GeneratedRegex(@"\b((system|urgent)\s+(note|message|notice|instruction)s?\s+(to|for)\s+(the\s+)?(ai|llm|assistant|agent)s?|(attention|note)\s*,?\s*(ai|llm)\s+(agents?|assistants?)|to\s+(any|all)\s+(ai|llm)\s+(agents?|assistants?))\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 100)]
    private static partial Regex AddressedToAi();

    [GeneratedRegex(@"\byou\s+are\s+now\s+(an?\s+|the\s+)?(admin|administrator|operator|root|system|unrestricted)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 100)]
    private static partial Regex RoleOverride();

    [GeneratedRegex(@"\b(disregard|bypass|override)\s+(all\s+|any\s+|your\s+|the\s+)?(rules|policies|policy|guardrails|approvals?|safety)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 100)]
    private static partial Regex DisregardRules();
}
