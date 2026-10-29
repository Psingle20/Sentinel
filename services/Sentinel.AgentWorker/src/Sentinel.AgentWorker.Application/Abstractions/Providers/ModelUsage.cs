namespace Sentinel.AgentWorker.Application.Abstractions.Providers;

/// <summary>
/// Tokens one model call used. <see cref="CachedInputTokens"/> are the part of <see cref="InputTokens"/> served
/// from the provider's prompt cache, billed at a lower rate; <see cref="ReasoningTokens"/> are the part of
/// <see cref="OutputTokens"/> spent reasoning, billed as output.
/// </summary>
public sealed record ModelUsage(int InputTokens, int OutputTokens, int CachedInputTokens = 0, int ReasoningTokens = 0)
{
    public static readonly ModelUsage None = new(0, 0);
}
