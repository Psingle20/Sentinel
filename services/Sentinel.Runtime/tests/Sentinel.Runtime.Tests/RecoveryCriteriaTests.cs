using Sentinel.Runtime.Domain.Incidents;
using Shouldly;

namespace Sentinel.Runtime.Tests;

/// <summary>The recovery rule (Phase 06): judged over the final third of the window, against the baseline before the action, never recovered on incomplete telemetry.</summary>
public sealed class RecoveryCriteriaTests
{
    private static readonly DateTimeOffset From = new(2026, 10, 29, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset To = From.AddMinutes(9);

    [Fact]
    public void Signals_that_hold_through_the_final_third_of_the_window_are_recovered()
    {
        RecoveryJudgement judgement = RecoveryCriteria.Judge(From, To, [Reading(baseline: 3, 2.5, 0.4, 0.3, 0.2)]);

        judgement.Trustworthy.ShouldBeTrue();
        judgement.Result.ShouldBe(RecoveryResult.Recovered);
        judgement.Measurements.ShouldHaveSingleItem().Met.ShouldBe(true);
        judgement.Measurements[0].BaselineValue.ShouldBe(3);
        judgement.Reason.ShouldBeNull();
    }

    [Fact]
    public void Telemetry_that_does_not_cover_the_window_or_a_plan_without_criteria_is_inconclusive()
    {
        RecoveryJudgement incomplete = RecoveryCriteria.Judge(From, To, [Reading(baseline: null) with { CoverageProblem = "slo_burn_rate observed through 09:05, Partial" }]);
        RecoveryJudgement empty = RecoveryCriteria.Judge(From, To, [Reading(baseline: null)]);
        RecoveryJudgement unstated = RecoveryCriteria.Judge(From, To, []);

        incomplete.Trustworthy.ShouldBeFalse();
        incomplete.Result.ShouldBe(RecoveryResult.Inconclusive);
        incomplete.Reason.ShouldBe("slo_burn_rate observed through 09:05, Partial");
        empty.Reason.ShouldBe("slo_burn_rate has no samples in the window");
        unstated.Reason.ShouldBe("the plan states no recovery criteria");
    }

    [Fact]
    public void An_unmet_window_is_unchanged_degraded_or_worse_than_before_the_action()
    {
        RecoveryCriteria.Judge(From, To, [Reading(baseline: null, 3, 3, 3)]).Result.ShouldBe(RecoveryResult.Unchanged);
        RecoveryCriteria.Judge(From, To, [Reading(baseline: null, 3, 4, 5)]).Result.ShouldBe(RecoveryResult.Degraded);
        RecoveryCriteria.Judge(From, To, [Reading(baseline: 2, 2, 3, 4)]).Result.ShouldBe(RecoveryResult.CompensationRequired);
    }

    // Samples spread evenly over the window; the burn rate must stay below 1.
    private static SignalReading Reading(double? baseline, params double[] values) => new(
        "checkout-service",
        "slo_burn_rate",
        RecoveryComparison.LessThan,
        1,
        [.. values.Select((value, index) => new MetricSample(From + (To - From) * (index + 1) / values.Length, value))],
        baseline,
        null);
}
