using System.Text.Json.Serialization;

namespace Sentinel.Contracts.Primitives.Identifiers;

[JsonConverter(typeof(StringIdJsonConverter<EnvironmentId>))]
public readonly record struct EnvironmentId : IStronglyTypedId<EnvironmentId, string>
{
    private EnvironmentId(string value)
    {
        Value = IdGuard.Slug(value, nameof(EnvironmentId));
    }

    public string Value { get; }

    public static EnvironmentId From(string value) => new(value);

    public override string ToString() => Value;
}
