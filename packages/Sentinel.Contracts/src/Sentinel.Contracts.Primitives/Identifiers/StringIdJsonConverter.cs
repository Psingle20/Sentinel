using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sentinel.Contracts.Primitives.Identifiers;

public sealed class StringIdJsonConverter<TId> : JsonConverter<TId>
    where TId : struct, IStronglyTypedId<TId, string>
{
    public override TId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        string value = reader.TokenType == JsonTokenType.String
            ? reader.GetString()!
            : throw new JsonException($"{typeof(TId).Name} must be a JSON string.");

        try
        {
            return TId.From(value);
        }
        catch (ArgumentException exception)
        {
            throw new JsonException(exception.Message, exception);
        }
    }

    public override void Write(Utf8JsonWriter writer, TId value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteStringValue(value.Value);
    }
}
