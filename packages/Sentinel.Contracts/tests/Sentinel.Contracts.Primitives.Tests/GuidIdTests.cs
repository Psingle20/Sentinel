using System.Text.Json;
using Sentinel.Contracts.Primitives.Identifiers;
using Shouldly;

namespace Sentinel.Contracts.Primitives.Tests;

public sealed class GuidIdTests
{
    private const string KnownGuid = "0192f0c4-5b6e-7a3c-9d2e-1f4a5b6c7d8e";

    [Fact]
    public void New_creates_a_time_ordered_version_7_identifier()
    {
        var id = IncidentId.New();

        id.Value.Version.ShouldBe(7);
    }

    [Fact]
    public void New_creates_distinct_identifiers()
    {
        IncidentId.New().ShouldNotBe(IncidentId.New());
    }

    [Fact]
    public void From_rejects_an_empty_guid()
    {
        Should.Throw<ArgumentException>(() => IncidentId.From(Guid.Empty));
    }

    [Fact]
    public void Serializes_as_a_plain_guid_string_and_round_trips()
    {
        var id = IncidentId.From(Guid.Parse(KnownGuid));

        string json = JsonSerializer.Serialize(id);

        json.ShouldBe($"\"{KnownGuid}\"");
        JsonSerializer.Deserialize<IncidentId>(json).ShouldBe(id);
    }

    [Theory]
    [InlineData("\"00000000-0000-0000-0000-000000000000\"")]
    [InlineData("\"not-a-guid\"")]
    [InlineData("42")]
    [InlineData("null")]
    public void Deserialization_rejects_invalid_values(string json)
    {
        Should.Throw<JsonException>(() => JsonSerializer.Deserialize<IncidentId>(json));
    }
}
