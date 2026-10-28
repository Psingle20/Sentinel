using System.Text.Json;
using System.Text.Json.Serialization;

namespace CleanArchitecture.BuildingBlocks.Infrastructure.Messaging;

/// <summary>Payload serialization shared by producers and consumers (ADR-012: camelCase, enums as strings).</summary>
internal static class MessageSerializer
{
    internal static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    internal static string Serialize(object message, Type type) => JsonSerializer.Serialize(message, type, Options);

    internal static object Deserialize(string payload, Type type) =>
        JsonSerializer.Deserialize(payload, type, Options)
        ?? throw new JsonException("The payload deserialized to null.");

    internal static string SerializeHeaders(IReadOnlyDictionary<string, string> headers) =>
        JsonSerializer.Serialize(headers, Options);

    internal static IReadOnlyDictionary<string, string> DeserializeHeaders(string json) =>
        JsonSerializer.Deserialize<Dictionary<string, string>>(json, Options) ?? [];
}
