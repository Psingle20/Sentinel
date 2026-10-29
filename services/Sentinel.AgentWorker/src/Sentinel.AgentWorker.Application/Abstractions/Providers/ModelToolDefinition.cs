namespace Sentinel.AgentWorker.Application.Abstractions.Providers;

/// <summary>A tool the model may request; <see cref="ParametersSchema"/> is a JSON Schema object.</summary>
public sealed record ModelToolDefinition(string Name, string Description, string ParametersSchema);
