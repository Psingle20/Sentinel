using System.Globalization;
using Microsoft.Extensions.Options;
using Sentinel.AgentWorker.Application.Abstractions.Persistence;
using Sentinel.AgentWorker.Application.Abstractions.Providers;
using Sentinel.AgentWorker.Application.Options;

namespace Sentinel.AgentWorker.Application.Investigations;

/// <summary>
/// Refuses a priced model once recorded spend reaches a <see cref="ModelBudgetOptions"/> cap. Spend counts executions
/// already finished, so investigations running at the same time can overshoot a cap by their own cost.
/// </summary>
public sealed class ModelSpendGuard(IModelSpend spend, IOptionsMonitor<ModelBudgetOptions> budget, TimeProvider time)
{
    /// <summary>Why <paramref name="model"/> may not be called now, or null when it may.</summary>
    public async Task<string?> RefusalAsync(IModelProvider model, ModelPrice price, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(price);

        ModelBudgetOptions limits = budget.CurrentValue;
        if (price.IsFree || limits is { TotalLimit: null, DailyLimit: null })
        {
            return null;
        }

        if (limits.DailyLimit is { } daily)
        {
            DateTimeOffset midnight = new(time.GetUtcNow().UtcDateTime.Date, TimeSpan.Zero);
            double today = await spend.SpentSinceAsync(midnight, cancellationToken);
            if (today >= daily)
            {
                return Refusal("daily", today, daily, model);
            }
        }

        if (limits.TotalLimit is { } total)
        {
            double all = await spend.SpentSinceAsync(DateTimeOffset.MinValue, cancellationToken);
            if (all >= total)
            {
                return Refusal("total", all, total, model);
            }
        }

        return null;
    }

    private static string Refusal(string cap, double spent, double limit, IModelProvider model) =>
        string.Create(CultureInfo.InvariantCulture, $"The {cap} model spend cap is reached (${spent:F2} of ${limit:F2}), so {model.Provider} ({model.Model}) was not called.");
}
