using System.Text.Json.Serialization;

namespace Sentinel.Contracts.Primitives.Identifiers;

[JsonConverter(typeof(StringIdJsonConverter<TenantId>))]
public readonly record struct TenantId : IStronglyTypedId<TenantId, string>
{
    private TenantId(string value)
    {
        Value = IdGuard.Slug(value, nameof(TenantId));
    }

    public string Value { get; }

    public static TenantId From(string value) => new(value);

    public override string ToString() => Value;
}
