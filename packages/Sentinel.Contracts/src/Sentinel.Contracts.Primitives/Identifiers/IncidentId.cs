using System.Text.Json.Serialization;

namespace Sentinel.Contracts.Primitives.Identifiers;

[JsonConverter(typeof(GuidIdJsonConverter<IncidentId>))]
public readonly record struct IncidentId : IStronglyTypedId<IncidentId, Guid>
{
    private IncidentId(Guid value)
    {
        Value = IdGuard.NotEmpty(value, nameof(IncidentId));
    }

    public Guid Value { get; }

    public static IncidentId New() => new(Guid.CreateVersion7());

    public static IncidentId From(Guid value) => new(value);

    public override string ToString() => Value.ToString();
}
