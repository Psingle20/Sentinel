using System.Text.Json.Serialization;

namespace Sentinel.Contracts.Primitives.Identifiers;

[JsonConverter(typeof(GuidIdJsonConverter<PlanId>))]
public readonly record struct PlanId : IStronglyTypedId<PlanId, Guid>
{
    private PlanId(Guid value)
    {
        Value = IdGuard.NotEmpty(value, nameof(PlanId));
    }

    public Guid Value { get; }

    public static PlanId New() => new(Guid.CreateVersion7());

    public static PlanId From(Guid value) => new(value);

    public override string ToString() => Value.ToString();
}
