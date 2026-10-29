namespace Sentinel.AgentWorker.Application.Abstractions.Providers;

/// <summary>One model's list prices in US dollars per million tokens; estimates only, the provider's bill is the truth.</summary>
public sealed record ModelPrice(double InputPerMillionTokens, double CachedInputPerMillionTokens, double OutputPerMillionTokens)
{
    public bool IsFree => InputPerMillionTokens <= 0 && CachedInputPerMillionTokens <= 0 && OutputPerMillionTokens <= 0;

    public double Estimate(ModelUsage usage)
    {
        ArgumentNullException.ThrowIfNull(usage);

        int cached = Math.Min(usage.CachedInputTokens, usage.InputTokens);
        return ((usage.InputTokens - cached) * InputPerMillionTokens
            + cached * CachedInputPerMillionTokens
            + usage.OutputTokens * OutputPerMillionTokens) / 1_000_000;
    }
}
