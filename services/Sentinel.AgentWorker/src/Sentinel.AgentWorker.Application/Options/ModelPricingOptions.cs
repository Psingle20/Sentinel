using System.Diagnostics;
using System.Diagnostics.Metrics;
using Sentinel.AgentWorker.Application.Abstractions.Providers;

namespace Sentinel.AgentWorker.Application.Options;

/// <summary>Configured as <c>Model:Pricing</c>; estimates only, in US dollars per million tokens.</summary>
public sealed class ModelPricingOptions
{
    public const string Section = "Model:Pricing";

    public double InputPerMillionTokens { get; set; }

    public double OutputPerMillionTokens { get; set; }

    public double Estimate(ModelUsage usage)
    {
        ArgumentNullException.ThrowIfNull(usage);

        return (usage.InputTokens * InputPerMillionTokens + usage.OutputTokens * OutputPerMillionTokens) / 1_000_000;
    }
}
