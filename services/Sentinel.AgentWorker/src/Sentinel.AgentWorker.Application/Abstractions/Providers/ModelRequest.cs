namespace Sentinel.AgentWorker.Application.Abstractions.Providers;

/// <summary>
/// One model call. <see cref="OutputSchema"/> is the JSON Schema the final answer must follow; a provider that
/// supports structured output sends it with the tools, so the answer is shaped by the API rather than by the prompt alone.
/// </summary>
public sealed record ModelRequest(
    string PromptVersion,
    IReadOnlyList<ModelMessage> Messages,
    IReadOnlyList<ModelToolDefinition> Tools,
    string OutputSchemaName,
    int MaxOutputTokens,
    string? OutputSchema = null);
