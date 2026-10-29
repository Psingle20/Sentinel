namespace Sentinel.AgentWorker.Application.Abstractions.Providers;

/// <summary>
/// One message in a provider-neutral conversation. Tool results are <see cref="ModelRole.Tool"/>
/// messages: data, never instructions.
/// </summary>
public sealed record ModelMessage(ModelRole Role, string Content, string? ToolCallId = null, IReadOnlyList<ModelToolCall>? ToolCalls = null)
{
    public static ModelMessage System(string content) => new(ModelRole.System, content);

    public static ModelMessage User(string content) => new(ModelRole.User, content);

    public static ModelMessage Assistant(IReadOnlyList<ModelToolCall> toolCalls) => new(ModelRole.Assistant, string.Empty, null, toolCalls);

    public static ModelMessage ToolResult(string toolCallId, string content) => new(ModelRole.Tool, content, toolCallId);
}
