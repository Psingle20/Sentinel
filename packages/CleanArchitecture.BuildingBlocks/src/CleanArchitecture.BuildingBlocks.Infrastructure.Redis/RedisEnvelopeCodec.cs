using System.Globalization;
using CleanArchitecture.BuildingBlocks.Infrastructure.Messaging;
using StackExchange.Redis;

namespace CleanArchitecture.BuildingBlocks.Infrastructure.Redis;

/// <summary>Maps envelopes to and from Redis stream entry fields.</summary>
internal static class RedisEnvelopeCodec
{
    internal const string MessageIdField = "id";
    internal const string MessageTypeField = "type";
    internal const string SchemaVersionField = "version";
    internal const string OccurredAtField = "occurred";
    internal const string ProducerField = "producer";
    internal const string CorrelationIdField = "correlation";
    internal const string CausationIdField = "causation";
    internal const string HeadersField = "headers";
    internal const string PayloadField = "payload";

    internal static NameValueEntry[] Encode(MessageEnvelope envelope)
    {
        var fields = new List<NameValueEntry>
        {
            new(MessageIdField, envelope.MessageId.ToString()),
            new(MessageTypeField, envelope.MessageType),
            new(SchemaVersionField, envelope.SchemaVersion),
            new(OccurredAtField, envelope.OccurredAt.ToString("O", CultureInfo.InvariantCulture)),
            new(ProducerField, envelope.Producer),
            new(HeadersField, MessageSerializer.SerializeHeaders(envelope.Headers)),
            new(PayloadField, envelope.Payload)
        };

        if (envelope.CorrelationId is not null)
        {
            fields.Add(new NameValueEntry(CorrelationIdField, envelope.CorrelationId));
        }

        if (envelope.CausationId is not null)
        {
            fields.Add(new NameValueEntry(CausationIdField, envelope.CausationId));
        }

        return [.. fields];
    }

    /// <exception cref="FormatException">The entry is missing a required field or a field is malformed.</exception>
    internal static MessageEnvelope Decode(StreamEntry entry)
    {
        Dictionary<string, string> fields = entry.Values.ToDictionary(field => field.Name.ToString(), field => field.Value.ToString());

        return new MessageEnvelope(
            Guid.Parse(Required(fields, MessageIdField)),
            Required(fields, MessageTypeField),
            int.Parse(Required(fields, SchemaVersionField), CultureInfo.InvariantCulture),
            DateTimeOffset.Parse(Required(fields, OccurredAtField), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
            Required(fields, ProducerField),
            fields.GetValueOrDefault(CorrelationIdField),
            fields.GetValueOrDefault(CausationIdField),
            fields.TryGetValue(HeadersField, out string? headers) ? MessageSerializer.DeserializeHeaders(headers) : new Dictionary<string, string>(),
            Required(fields, PayloadField));
    }

    private static string Required(Dictionary<string, string> fields, string name) =>
        fields.TryGetValue(name, out string? value) && !string.IsNullOrEmpty(value)
            ? value
            : throw new FormatException($"Stream entry is missing the '{name}' field.");
}
