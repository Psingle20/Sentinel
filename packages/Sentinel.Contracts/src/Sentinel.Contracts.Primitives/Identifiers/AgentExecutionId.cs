using System.Text.Json.Serialization;

namespace Sentinel.Contracts.Primitives.Identifiers;

[JsonConverter(typeof(GuidIdJsonConverter<AgentExecutionId>))]
public readonly record struct AgentExecutionId : IStronglyTypedId<AgentExecutionId, Guid>
{
    private AgentExecutionId(Guid value)
    {
        Value = IdGuard.NotEmpty(value, nameof(AgentExecutionId));
    }

    public Guid Value { get; }

    public static AgentExecutionId New() => new(Guid.CreateVersion7());

    public static AgentExecutionId From(Guid value) => new(value);

    public override string ToString() => Value.ToString();
}
