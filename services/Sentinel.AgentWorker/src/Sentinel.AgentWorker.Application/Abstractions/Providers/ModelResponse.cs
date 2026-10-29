namespace Sentinel.AgentWorker.Application.Abstractions.Providers;

public sealed record ModelResponse(ModelFinish Finish, IReadOnlyList<ModelToolCall> ToolCalls, string? Content, ModelUsage Usage, string? Error = null)
{
    public static ModelResponse Failed(ModelFinish finish, string error) => new(finish, [], null, ModelUsage.None, error);
}
