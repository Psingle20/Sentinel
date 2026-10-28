using System.Text.Json.Serialization;

namespace Sentinel.Contracts.Primitives.Identifiers;

[JsonConverter(typeof(GuidIdJsonConverter<InvestigationId>))]
public readonly record struct InvestigationId : IStronglyTypedId<InvestigationId, Guid>
{
    private InvestigationId(Guid value)
    {
        Value = IdGuard.NotEmpty(value, nameof(InvestigationId));
    }

    public Guid Value { get; }

    public static InvestigationId New() => new(Guid.CreateVersion7());

    public static InvestigationId From(Guid value) => new(value);

    public override string ToString() => Value.ToString();
}
