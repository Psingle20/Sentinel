using System.Text.Json.Serialization;

namespace Sentinel.Contracts.Primitives.Identifiers;

[JsonConverter(typeof(GuidIdJsonConverter<AttestationId>))]
public readonly record struct AttestationId : IStronglyTypedId<AttestationId, Guid>
{
    private AttestationId(Guid value)
    {
        Value = IdGuard.NotEmpty(value, nameof(AttestationId));
    }

    public Guid Value { get; }

    public static AttestationId New() => new(Guid.CreateVersion7());

    public static AttestationId From(Guid value) => new(value);

    public override string ToString() => Value.ToString();
}
