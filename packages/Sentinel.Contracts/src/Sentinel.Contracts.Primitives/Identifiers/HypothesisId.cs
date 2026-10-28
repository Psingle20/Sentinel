using System.Text.Json.Serialization;

namespace Sentinel.Contracts.Primitives.Identifiers;

[JsonConverter(typeof(GuidIdJsonConverter<HypothesisId>))]
public readonly record struct HypothesisId : IStronglyTypedId<HypothesisId, Guid>
{
    private HypothesisId(Guid value)
    {
        Value = IdGuard.NotEmpty(value, nameof(HypothesisId));
    }

    public Guid Value { get; }

    public static HypothesisId New() => new(Guid.CreateVersion7());

    public static HypothesisId From(Guid value) => new(value);

    public override string ToString() => Value.ToString();
}
