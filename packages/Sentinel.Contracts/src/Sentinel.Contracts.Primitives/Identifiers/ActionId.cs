using System.Text.Json.Serialization;

namespace Sentinel.Contracts.Primitives.Identifiers;

[JsonConverter(typeof(GuidIdJsonConverter<ActionId>))]
public readonly record struct ActionId : IStronglyTypedId<ActionId, Guid>
{
    private ActionId(Guid value)
    {
        Value = IdGuard.NotEmpty(value, nameof(ActionId));
    }

    public Guid Value { get; }

    public static ActionId New() => new(Guid.CreateVersion7());

    public static ActionId From(Guid value) => new(value);

    public override string ToString() => Value.ToString();
}
