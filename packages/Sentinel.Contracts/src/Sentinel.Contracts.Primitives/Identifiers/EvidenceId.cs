using System.Text.Json.Serialization;

namespace Sentinel.Contracts.Primitives.Identifiers;

[JsonConverter(typeof(GuidIdJsonConverter<EvidenceId>))]
public readonly record struct EvidenceId : IStronglyTypedId<EvidenceId, Guid>
{
    private EvidenceId(Guid value)
    {
        Value = IdGuard.NotEmpty(value, nameof(EvidenceId));
    }

    public Guid Value { get; }

    public static EvidenceId New() => new(Guid.CreateVersion7());

    public static EvidenceId From(Guid value) => new(value);

    public override string ToString() => Value.ToString();
}
