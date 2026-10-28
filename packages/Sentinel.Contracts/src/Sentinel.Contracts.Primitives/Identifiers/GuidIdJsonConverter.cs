using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sentinel.Contracts.Primitives.Identifiers;

public sealed class GuidIdJsonConverter<TId> : JsonConverter<TId>
    where TId : struct, IStronglyTypedId<TId, Guid>
{
    public override TId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String || !reader.TryGetGuid(out Guid value))
        {
            throw new JsonException($"{typeof(TId).Name} must be a GUID string.");
        }

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
