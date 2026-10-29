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
