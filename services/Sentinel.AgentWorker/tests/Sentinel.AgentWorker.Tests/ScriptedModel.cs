using Sentinel.AgentWorker.Application.Abstractions.Providers;

namespace Sentinel.AgentWorker.Tests;

/// <summary>A model that asks for one fixed tool call, then abstains, and remembers which tools it was offered.</summary>
internal sealed class ScriptedModel(ModelToolCall call) : IModelProvider
{
    public IReadOnlyList<string> OfferedTools { get; private set; } = [];

    public string Provider => "scripted";

    public string Model => "scripted";

    public string? ModelVersion => null;

    public Task<ModelResponse> CompleteAsync(ModelRequest request, CancellationToken cancellationToken)
    {
        OfferedTools = [.. request.Tools.Select(tool => tool.Name)];
        return Task.FromResult(request.Messages.Any(message => message.Role == ModelRole.Tool)
            ? new ModelResponse(ModelFinish.Final, [], """{"abstain":true,"abstentionReason":"done"}""", new ModelUsage(10, 10))
            : new ModelResponse(ModelFinish.ToolCalls, [call], null, new ModelUsage(10, 10)));
    }
}
