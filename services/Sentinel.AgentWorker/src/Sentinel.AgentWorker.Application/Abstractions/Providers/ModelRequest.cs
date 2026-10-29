namespace Sentinel.AgentWorker.Application.Abstractions.Providers;

public sealed record ModelRequest(
    string PromptVersion,
    IReadOnlyList<ModelMessage> Messages,
    IReadOnlyList<ModelToolDefinition> Tools,
    string OutputSchemaName,
    int MaxOutputTokens);
