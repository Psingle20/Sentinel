using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Sentinel.AgentWorker.Application.Abstractions.Persistence;
using Sentinel.AgentWorker.Application.Abstractions.Providers;
using Sentinel.AgentWorker.Application.Diagnostics;
using Sentinel.AgentWorker.Application.Investigations;
using Sentinel.AgentWorker.Application.Options;
using Sentinel.AgentWorker.Infrastructure.Models;
using Sentinel.Contracts.Investigations;
using Shouldly;

namespace Sentinel.AgentWorker.Tests;

/// <summary>Model cost estimates and the spend caps that protect a limited model credit.</summary>
public sealed class ModelSpendTests
{
    private static readonly ModelPrice Spark = new(1.25, 0.15, 4.25);

    private static readonly DateTimeOffset Now = new(2026, 10, 29, 15, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Cached_input_is_charged_at_its_own_rate_and_reasoning_as_output()
    {
        double cost = Spark.Estimate(new ModelUsage(InputTokens: 1_000_000, OutputTokens: 100_000, CachedInputTokens: 800_000, ReasoningTokens: 90_000));

        cost.ShouldBe(0.2 * 1.25 + 0.8 * 0.15 + 0.1 * 4.25, tolerance: 1e-9);
    }

    [Fact]
    public async Task A_priced_model_is_refused_once_the_daily_or_total_cap_is_reached_and_allowed_below_them()
    {
        var spend = new FixedSpend(today: 14.99, total: 60);
        ModelSpendGuard guard = Guard(spend, total: 100, daily: 15);
        (await guard.RefusalAsync(new PricedModel(), Spark, CancellationToken.None)).ShouldBeNull();

        spend.Today = 15;
        (await guard.RefusalAsync(new PricedModel(), Spark, CancellationToken.None)).ShouldNotBeNull().ShouldContain("daily model spend cap is reached ($15.00 of $15.00)");

        spend.Today = 1;
        spend.Total = 100.5;
        (await guard.RefusalAsync(new PricedModel(), Spark, CancellationToken.None)).ShouldNotBeNull().ShouldContain("total model spend cap is reached ($100.50 of $100.00)");
    }

    [Fact]
    public async Task An_expensive_model_has_its_own_daily_cap_while_the_others_stay_available()
    {
        var spend = new FixedSpend(today: 6, total: 20);
        spend.TodayByModel["muse-wonder-1.0"] = 5;
        var budget = new ModelBudgetOptions { TotalLimit = 100, DailyLimit = 15 };
        budget.ModelDailyLimits["muse-wonder-1.0"] = 5;
        var guard = new ModelSpendGuard(spend, new StaticBudget(budget), new FakeTimeProvider(Now));

        (await guard.RefusalAsync(new PricedModel(model: "muse-wonder-1.0"), Spark, CancellationToken.None))
            .ShouldNotBeNull().ShouldContain("daily spend cap for muse-wonder-1.0 is reached ($5.00 of $5.00)");
        (await guard.RefusalAsync(new PricedModel(), Spark, CancellationToken.None)).ShouldBeNull("muse-spark-1.3 has no cap of its own");
    }

    [Fact]
    public async Task A_free_model_is_never_refused_or_queried_and_an_uncapped_deployment_never_refuses()
    {
        var spend = new FixedSpend(today: 1_000, total: 1_000);

        (await Guard(spend, total: 100, daily: 15).RefusalAsync(new PricedModel(), new ModelPrice(0, 0, 0), CancellationToken.None)).ShouldBeNull();
        spend.Queries.ShouldBe(0);
        (await Guard(spend, total: null, daily: null).RefusalAsync(new PricedModel(), Spark, CancellationToken.None)).ShouldBeNull();
    }

    [Fact]
    public async Task At_the_cap_an_investigation_abstains_without_calling_the_model()
    {
        var telemetry = Telemetry.ConnectionLeak();
        var model = new PricedModel();

        InvestigationRun run = await Investigator(model, Guard(new FixedSpend(today: 0, total: 100), total: 100, daily: null), telemetry)
            .InvestigateAsync(telemetry.Command(detectedAt: 70), shadow: false, CancellationToken.None);

        run.Completed.Outcome.ShouldBe(InvestigationOutcome.Abstained);
        run.Execution.AbstentionReason.ShouldNotBeNull().ShouldContain("total model spend cap");
        model.Calls.ShouldBe(0);
    }

    [Fact]
    public async Task A_truncated_answer_gets_one_repair_and_every_call_is_costed_at_the_models_price()
    {
        var telemetry = Telemetry.ConnectionLeak();
        var model = new PricedModel(truncateFirstAnswer: true);

        InvestigationRun run = await Investigator(model, Guard(new FixedSpend(today: 0, total: 0), total: 100, daily: 15), telemetry)
            .InvestigateAsync(telemetry.Command(detectedAt: 70), shadow: false, CancellationToken.None);

        run.Completed.Outcome.ShouldBe(InvestigationOutcome.Completed);
        run.Execution.SchemaFailures.ShouldBe(1);
        run.Execution.EstimatedCost.ShouldBe(model.Usage.Sum(Spark.Estimate), tolerance: 1e-12);
        run.Execution.EstimatedCost.ShouldBeGreaterThan(0);
        model.Schemas.ShouldAllBe(schema => schema == FindingsValidator.OutputJsonSchema);
    }

    private static ModelSpendGuard Guard(IModelSpend spend, double? total, double? daily) =>
        new(spend, new StaticBudget(new ModelBudgetOptions { TotalLimit = total, DailyLimit = daily }), new FakeTimeProvider(Now));

    private static ModelInvestigator Investigator(IModelProvider model, ModelSpendGuard guard, Telemetry telemetry) =>
        new(
            new SingleRouter(model),
            Harness.AllTools(telemetry),
            Harness.Catalog,
            Options.Create(new ModelPricingOptions()),
            new AgentWorkerTelemetry(),
            TimeProvider.System,
            NullLogger<ModelInvestigator>.Instance,
            spend: guard);

    private sealed class FixedSpend(double today, double total) : IModelSpend
    {
        public double Today { get; set; } = today;

        public double Total { get; set; } = total;

        public Dictionary<string, double> TodayByModel { get; } = new(StringComparer.Ordinal);

        public int Queries { get; private set; }

        public Task<double> SpentSinceAsync(DateTimeOffset since, string? model, CancellationToken cancellationToken)
        {
            Queries++;
            if (model is not null)
            {
                return Task.FromResult(TodayByModel.GetValueOrDefault(model));
            }

            return Task.FromResult(since == DateTimeOffset.MinValue ? Total : Today);
        }
    }

    private sealed class StaticBudget(ModelBudgetOptions options) : IOptionsMonitor<ModelBudgetOptions>
    {
        public ModelBudgetOptions CurrentValue => options;

        public ModelBudgetOptions Get(string? name) => options;

        public IDisposable? OnChange(Action<ModelBudgetOptions, string?> listener) => null;
    }

    private sealed class SingleRouter(IModelProvider model) : IModelRouter
    {
        public IReadOnlyCollection<string> Configured => [model.Provider];

        public IModelProvider For(string role) => model;
    }

    /// <summary>The deterministic double behind a list price, optionally cutting off its first final answer.</summary>
    private sealed class PricedModel(bool truncateFirstAnswer = false, string model = "muse-spark-1.3") : IModelProvider
    {
        private readonly DeterministicModelProvider _inner = new(Options.Create(new TestDoubleOptions()));
        private bool _truncated;

        public int Calls { get; private set; }

        public List<ModelUsage> Usage { get; } = [];

        public List<string?> Schemas { get; } = [];

        public string Provider => "meta";

        public string Model => model;

        public string? ModelVersion => null;

        public ModelPrice Price => Spark;

        public async Task<ModelResponse> CompleteAsync(ModelRequest request, CancellationToken cancellationToken)
        {
            Calls++;
            Schemas.Add(request.OutputSchema);
            ModelResponse response = await _inner.CompleteAsync(request, cancellationToken);
            if (truncateFirstAnswer && !_truncated && response.Finish == ModelFinish.Final)
            {
                _truncated = true;
                response = new ModelResponse(ModelFinish.Truncated, [], null, response.Usage with { ReasoningTokens = 300 }, "The answer reached the output limit after 300 reasoning tokens.");
            }

            Usage.Add(response.Usage);
            return response;
        }
    }
}
