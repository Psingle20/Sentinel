using System.Text.Json;
using Sentinel.Contracts.Primitives.Identifiers;
using Shouldly;

namespace Sentinel.Contracts.Primitives.Tests;

public sealed class StringIdTests
{
    [Theory]
    [InlineData("a")]
    [InlineData("acme")]
    [InlineData("checkout-service")]
    [InlineData("prod-eu-1")]
    public void Slug_identifiers_accept_valid_slugs(string value)
    {
        ServiceId.From(value).Value.ShouldBe(value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("-checkout")]
    [InlineData("checkout-")]
    [InlineData("Checkout")]
    [InlineData("check out")]
    [InlineData("checkout_service")]
    public void Slug_identifiers_reject_invalid_slugs(string value)
    {
        Should.Throw<ArgumentException>(() => TenantId.From(value));
    }

    [Fact]
    public void Slug_identifiers_reject_values_longer_than_63_characters()
    {
        Should.Throw<ArgumentException>(() => EnvironmentId.From(new string('a', 64)));
    }

    [Fact]
    public void Observation_identifiers_accept_opaque_printable_values()
    {
        const string value = "prometheus:checkout:latency_p99:1730073600";

        ObservationId.From(value).Value.ShouldBe(value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("has space")]
    [InlineData("tab\tinside")]
    public void Observation_identifiers_reject_blank_or_whitespace_values(string value)
    {
        Should.Throw<ArgumentException>(() => ObservationId.From(value));
    }

    [Fact]
    public void Observation_identifiers_reject_values_longer_than_256_characters()
    {
        Should.Throw<ArgumentException>(() => ObservationId.From(new string('x', 257)));
    }

    [Fact]
    public void Serializes_as_a_plain_string_and_round_trips()
    {
        var id = TenantId.From("acme");

        string json = JsonSerializer.Serialize(id);

        json.ShouldBe("\"acme\"");
        JsonSerializer.Deserialize<TenantId>(json).ShouldBe(id);
    }

    [Theory]
    [InlineData("\"Acme\"")]
    [InlineData("42")]
    [InlineData("null")]
    public void Deserialization_rejects_invalid_values(string json)
    {
        Should.Throw<JsonException>(() => JsonSerializer.Deserialize<TenantId>(json));
    }
}
