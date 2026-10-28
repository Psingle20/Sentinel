using System.Text.Json.Serialization;

namespace Sentinel.Contracts.Primitives.Identifiers;

[JsonConverter(typeof(StringIdJsonConverter<ServiceId>))]
public readonly record struct ServiceId : IStronglyTypedId<ServiceId, string>
{
    private ServiceId(string value)
    {
        Value = IdGuard.Slug(value, nameof(ServiceId));
    }

    public string Value { get; }

    public static ServiceId From(string value) => new(value);

    public override string ToString() => Value;
}
