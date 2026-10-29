using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Runtime.Domain.Correlation;
using Shouldly;

namespace Sentinel.Runtime.Tests;

public sealed class SignalStreakTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 28, 9, 0, 0, TimeSpan.Zero);

    private static readonly DetectionRule Rule = new()
    {
        ServiceId = ServiceId.From("checkout-service"),
        MetricName = "slo_burn_rate",
        DetectionThreshold = 2.0,
        OnsetThreshold = 1.0,
        RequiredSamples = 2,
        ClearAfter = TimeSpan.FromSeconds(20)
    };

    private static SignalStreak Empty => new()
    {
        TenantId = TenantId.From("acme"),
        EnvironmentId = EnvironmentId.From("reference"),
        ServiceId = Rule.ServiceId,
        MetricName = Rule.MetricName
    };

    private static SignalStreak Feed(SignalStreak streak, params (int Second, double Value)[] samples) =>
        samples.Aggregate(streak, (current, sample) => current.Observe(Start.AddSeconds(sample.Second), sample.Value, Rule));

    [Fact]
    public void Onset_is_the_first_sample_above_one_and_detection_needs_two_samples_above_two()
    {
        SignalStreak streak = Feed(Empty, (0, 0.1), (5, 1.4), (10, 2.5));

        streak.OnsetAt.ShouldBe(Start.AddSeconds(5));
        streak.Fires(Rule).ShouldBeFalse();

        Feed(streak, (15, 6.0)).Fires(Rule).ShouldBeTrue();
    }

    [Fact]
    public void Duplicate_and_late_samples_change_nothing()
    {
        SignalStreak streak = Feed(Empty, (0, 3.0), (5, 3.0));

        streak.Observe(Start.AddSeconds(5), 0.1, Rule).ShouldBe(streak);
        streak.Observe(Start.AddSeconds(1), 0.1, Rule).ShouldBe(streak);
    }

    [Fact]
    public void A_dip_below_onset_resets_the_streak_and_a_sustained_dip_clears_it()
    {
        SignalStreak streak = Feed(Empty, (0, 3.0), (5, 0.5));

        streak.OnsetAt.ShouldBeNull();
        streak.ConsecutiveAboveDetection.ShouldBe(0);
        streak.HasCleared(Rule).ShouldBeFalse();
        Feed(streak, (15, 0.4), (25, 0.3)).HasCleared(Rule).ShouldBeTrue();
    }
}
