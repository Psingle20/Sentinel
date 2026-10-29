namespace Sentinel.AgentWorker.Application.Options;

/// <summary>
/// Configured as <c>Model:Budget</c>: hard caps, in US dollars, on the estimated spend of priced models, computed from
/// recorded executions. Once a cap is reached a priced model is not called and the investigation abstains, saying why.
/// Null leaves that cap off; the test double is free and never limited.
/// </summary>
public sealed class ModelBudgetOptions
{
    public const string Section = "Model:Budget";

    /// <summary>All recorded spend, since this deployment's executions began.</summary>
    public double? TotalLimit { get; set; }

    /// <summary>Spend since midnight UTC, so a runaway loop cannot use the total in one day.</summary>
    public double? DailyLimit { get; set; }

    /// <summary>A model's own spend since midnight UTC, by model name, for expensive models kept for demonstrations.</summary>
    public IDictionary<string, double> ModelDailyLimits { get; } = new Dictionary<string, double>(StringComparer.Ordinal);
}
