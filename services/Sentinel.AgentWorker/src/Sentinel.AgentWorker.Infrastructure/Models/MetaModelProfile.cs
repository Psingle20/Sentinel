using Sentinel.AgentWorker.Application.Abstractions.Providers;

namespace Sentinel.AgentWorker.Infrastructure.Models;

/// <summary>
/// One Meta model as Sentinel uses it, configured as <c>Model:Meta:Profiles:&lt;name&gt;</c>; the name is the provider a
/// role routes to (ADR-015). Prices are the model's list prices in US dollars per million tokens, for cost estimates and
/// the spend caps.
/// </summary>
public sealed class MetaModelProfile
{
    public string Model { get; set; } = string.Empty;

    public string? ModelVersion { get; set; }

    /// <summary>The API's <c>reasoning_effort</c>: <c>minimal</c>, <c>low</c>, <c>medium</c>, <c>high</c>, <c>xhigh</c> or <c>max</c>; null lets the model choose.</summary>
    public string? ReasoningEffort { get; set; }

    /// <summary>Output tokens allowed for reasoning on top of the answer's budget: reasoning is billed as output and counts against the same limit.</summary>
    public int ReasoningTokens { get; set; } = 8_000;

    public double InputPricePerMillionTokens { get; set; }

    public double CachedInputPricePerMillionTokens { get; set; }

    public double OutputPricePerMillionTokens { get; set; }

    public static IReadOnlyList<string> Efforts { get; } = ["minimal", "low", "medium", "high", "xhigh", "max"];

    public ModelPrice Price => new(InputPricePerMillionTokens, CachedInputPricePerMillionTokens, OutputPricePerMillionTokens);

    public bool IsValid() =>
        !string.IsNullOrWhiteSpace(Model)
        && (ReasoningEffort is null || Efforts.Contains(ReasoningEffort))
        && ReasoningTokens is >= 0 and <= 64_000
        && InputPricePerMillionTokens >= 0 && CachedInputPricePerMillionTokens >= 0 && OutputPricePerMillionTokens >= 0;
}
