namespace Sentinel.AgentWorker.Application.Abstractions.Providers;

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
