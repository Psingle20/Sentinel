using System.Text.Json;
using System.Text.Json.Serialization;
using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.Contracts.Messaging;

/// <summary>
/// The serialization every channel uses for Sentinel contracts: camelCase, enums as camelCase
/// strings, nulls omitted (ADR-012). It matches the building blocks' message serializer.
/// </summary>
public static class SentinelJson
{
    public static JsonSerializerOptions Options { get; } = CreateOptions();

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
        };
        options.MakeReadOnly(populateMissingResolver: true);
        return options;
    }
}
