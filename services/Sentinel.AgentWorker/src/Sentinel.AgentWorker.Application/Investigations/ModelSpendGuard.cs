using System.Globalization;
using Microsoft.Extensions.Options;
using Sentinel.AgentWorker.Application.Abstractions.Persistence;
using Sentinel.AgentWorker.Application.Abstractions.Providers;
using Sentinel.AgentWorker.Application.Options;

namespace Sentinel.AgentWorker.Application.Investigations;

/// <summary>
/// Refuses a priced model once recorded spend reaches a <see cref="ModelBudgetOptions"/> cap: the total, the day's, or
/// the model's own for the day. Spend counts executions already finished, so investigations running at the same time
/// can overshoot a cap by their own cost.
/// </summary>
public sealed class ModelSpendGuard(IModelSpend spend, IOptionsMonitor<ModelBudgetOptions> budget, TimeProvider time)
{
    /// <summary>Why <paramref name="model"/> may not be called now, or null when it may.</summary>
    public async Task<string?> RefusalAsync(IModelProvider model, ModelPrice price, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(price);

        ModelBudgetOptions limits = budget.CurrentValue;
        if (price.IsFree)
        {
            return null;
        }

        DateTimeOffset midnight = new(time.GetUtcNow().UtcDateTime.Date, TimeSpan.Zero);
        if (limits.DailyLimit is { } daily && await spend.SpentSinceAsync(midnight, null, cancellationToken) is var today && today >= daily)
        {
            return Refusal("daily model spend cap", today, daily, model);
        }

        if (limits.TotalLimit is { } total && await spend.SpentSinceAsync(DateTimeOffset.MinValue, null, cancellationToken) is var all && all >= total)
        {
            return Refusal("total model spend cap", all, total, model);
        }

        if (limits.ModelDailyLimits.TryGetValue(model.Model, out double own) && await spend.SpentSinceAsync(midnight, model.Model, cancellationToken) is var spent && spent >= own)
        {
            return Refusal($"daily spend cap for {model.Model}", spent, own, model);
        }

        return null;
    }

    private static string Refusal(string cap, double spent, double limit, IModelProvider model) =>
        string.Create(CultureInfo.InvariantCulture, $"The {cap} is reached (${spent:F2} of ${limit:F2}), so {model.Provider} ({model.Model}) was not called.");
}
