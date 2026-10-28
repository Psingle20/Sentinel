using CleanArchitecture.BuildingBlocks.Infrastructure.Messaging;
using CleanArchitecture.BuildingBlocks.Infrastructure.Redis;
using Shouldly;
using StackExchange.Redis;

namespace CleanArchitecture.BuildingBlocks.Infrastructure.Tests;

public sealed class RedisEnvelopeCodecTests
{
    private static readonly MessageEnvelope Envelope = new(
        Guid.Parse("0192f0c4-5b6e-7a3c-9d2e-1f4a5b6c7d8e"),
        "orders.order.placed",
        2,
        new DateTimeOffset(2026, 10, 28, 9, 30, 15, 123, TimeSpan.Zero),
        "orders",
        "flow-1",
        "cause-1",
        new Dictionary<string, string> { ["tenant"] = "acme" },
        """{"orderId":"0192f0c4-5b6e-7a3c-9d2e-1f4a5b6c7d8e"}""");

    private static StreamEntry Entry(NameValueEntry[] fields) => new("1-0", fields);

    [Fact]
    public void Envelopes_round_trip_through_stream_fields()
    {
        MessageEnvelope decoded = RedisEnvelopeCodec.Decode(Entry(RedisEnvelopeCodec.Encode(Envelope)));

        decoded.MessageId.ShouldBe(Envelope.MessageId);
        decoded.MessageType.ShouldBe(Envelope.MessageType);
        decoded.SchemaVersion.ShouldBe(2);
        decoded.OccurredAt.ShouldBe(Envelope.OccurredAt);
        decoded.Producer.ShouldBe("orders");
        decoded.CorrelationId.ShouldBe("flow-1");
        decoded.CausationId.ShouldBe("cause-1");
        decoded.Headers["tenant"].ShouldBe("acme");
        decoded.Payload.ShouldBe(Envelope.Payload);
    }

    [Fact]
    public void Optional_fields_may_be_absent()
    {
        MessageEnvelope withoutOptional = Envelope with { CorrelationId = null, CausationId = null };

        MessageEnvelope decoded = RedisEnvelopeCodec.Decode(Entry(RedisEnvelopeCodec.Encode(withoutOptional)));

        decoded.CorrelationId.ShouldBeNull();
        decoded.CausationId.ShouldBeNull();
    }

    [Fact]
    public void An_entry_missing_a_required_field_is_malformed()
    {
        NameValueEntry[] fields = [.. RedisEnvelopeCodec.Encode(Envelope).Where(field => field.Name != RedisEnvelopeCodec.PayloadField)];

        Should.Throw<FormatException>(() => RedisEnvelopeCodec.Decode(Entry(fields)))
            .Message.ShouldContain(RedisEnvelopeCodec.PayloadField);
    }
}
