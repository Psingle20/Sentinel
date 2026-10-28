namespace Sentinel.AgentWorker.Application.Models;

public enum ModelRole
{
    System,
    User,
    Assistant,
    Tool
}

/// <summary>A tool the model may request; <see cref="ParametersSchema"/> is a JSON Schema object.</summary>
public sealed record ModelToolDefinition(string Name, string Description, string ParametersSchema);

public sealed record ModelToolCall(string Id, string Name, string Arguments);

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

public sealed record ModelRequest(
    string PromptVersion,
    IReadOnlyList<ModelMessage> Messages,
    IReadOnlyList<ModelToolDefinition> Tools,
    string OutputSchemaName,
    int MaxOutputTokens);

public enum ModelFinish
{
    /// <summary>The model requested tool calls.</summary>
    ToolCalls,

    /// <summary>The model returned its final structured output in <see cref="ModelResponse.Content"/>.</summary>
    Final,

    Refused,
    Timeout,
    RateLimited,
    Error
}

public sealed record ModelUsage(int InputTokens, int OutputTokens)
{
    public static readonly ModelUsage None = new(0, 0);
}

public sealed record ModelResponse(ModelFinish Finish, IReadOnlyList<ModelToolCall> ToolCalls, string? Content, ModelUsage Usage, string? Error = null)
{
    public static ModelResponse Failed(ModelFinish finish, string error) => new(finish, [], null, ModelUsage.None, error);
}

/// <summary>
/// A Sentinel-owned model interface (ADR-015). Implementations translate to one provider's wire
/// format; they never run tools, validate findings or see Sentinel's domain.
/// </summary>
public interface IModelProvider
{
    string Provider { get; }

    string Model { get; }

    string? ModelVersion { get; }

    /// <summary>True when the same request always gets the same response, so a replay may claim determinism (Phase 07).</summary>
    bool Deterministic => false;

    Task<ModelResponse> CompleteAsync(ModelRequest request, CancellationToken cancellationToken);
}
