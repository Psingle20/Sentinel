using System.Text.Json.Serialization;

namespace Sentinel.Contracts.Primitives.Identifiers;

[JsonConverter(typeof(StringIdJsonConverter<ObservationId>))]
public readonly record struct ObservationId : IStronglyTypedId<ObservationId, string>
{
    private ObservationId(string value)
    {
        Value = IdGuard.Opaque(value, nameof(ObservationId));
    }

    public string Value { get; }

    public static ObservationId From(string value) => new(value);

    public override string ToString() => Value;
}
