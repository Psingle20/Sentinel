using System.Text.Json.Serialization;

namespace Sentinel.Contracts.Primitives.Identifiers;

[JsonConverter(typeof(GuidIdJsonConverter<DecisionId>))]
public readonly record struct DecisionId : IStronglyTypedId<DecisionId, Guid>
{
    private DecisionId(Guid value)
    {
        Value = IdGuard.NotEmpty(value, nameof(DecisionId));
    }

    public Guid Value { get; }

    public static DecisionId New() => new(Guid.CreateVersion7());

    public static DecisionId From(Guid value) => new(value);

    public override string ToString() => Value.ToString();
}
