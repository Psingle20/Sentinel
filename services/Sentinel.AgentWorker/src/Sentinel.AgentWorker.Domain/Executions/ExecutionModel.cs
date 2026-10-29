namespace Sentinel.AgentWorker.Domain.Executions;

/// <summary>The model and prompt an execution ran with; for an investigator in code, the code's own name and version.</summary>
public sealed record ExecutionModel(string Provider, string Model, string? ModelVersion, string PromptVersion);
