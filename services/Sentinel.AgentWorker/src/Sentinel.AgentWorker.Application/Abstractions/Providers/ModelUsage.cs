namespace Sentinel.AgentWorker.Application.Abstractions.Providers;

public sealed record ModelUsage(int InputTokens, int OutputTokens)
{
    public static readonly ModelUsage None = new(0, 0);
}
