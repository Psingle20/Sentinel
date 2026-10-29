using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using Sentinel.Contracts;
using Sentinel.Contracts.Messaging;
using Sentinel.Contracts.Observations;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Contracts.Queries;

namespace Sentinel.AgentWorker.Application.Tools;

internal static class ToolArguments
{
    public static bool TryString(JsonElement arguments, string name, out string? value)
    {
        value = arguments.ValueKind == JsonValueKind.Object && arguments.TryGetProperty(name, out JsonElement element) && element.ValueKind == JsonValueKind.String
            ? element.GetString()
            : null;
        return !string.IsNullOrWhiteSpace(value);
    }

    public static bool TryService(string value, out ServiceId service)
    {
        service = default;
        try
        {
            service = ServiceId.From(value);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    /// <summary>The requested range, clamped to the investigation window.</summary>
    public static TimeRange Range(JsonElement arguments, TimeRange window)
    {
        DateTimeOffset from = TryTime(arguments, "from") is { } requestedFrom && requestedFrom > window.From ? requestedFrom : window.From;
        DateTimeOffset to = TryTime(arguments, "to") is { } requestedTo && requestedTo < window.To ? requestedTo : window.To;
        return to > from ? new TimeRange(from, to) : window;
    }

    public static JsonObject Metadata(QueryMetadata metadata) => new()
    {
        ["observedThrough"] = metadata.ObservedThrough.ToString("O", CultureInfo.InvariantCulture),
        ["completeness"] = metadata.Completeness.ToString().ToLowerInvariant(),
        ["truncated"] = metadata.Truncated
    };

    public static bool IsPartial(QueryMetadata metadata, TimeRange range) =>
        metadata.Completeness != QueryCompleteness.Complete || metadata.Truncated || metadata.ObservedThrough < range.To;

    private static DateTimeOffset? TryTime(JsonElement arguments, string name) =>
        TryString(arguments, name, out string? value) && DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTimeOffset parsed)
            ? parsed
            : null;
}
