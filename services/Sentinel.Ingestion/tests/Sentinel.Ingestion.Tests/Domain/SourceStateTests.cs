using Sentinel.Ingestion.Domain.Sources;
using Shouldly;

namespace Sentinel.Ingestion.Tests.Domain;

/// <summary>A source's progress and health: what one pull, a failure or a recovery does to it.</summary>
public sealed class SourceStateTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 29, 9, 30, 0, TimeSpan.Zero);

    private static SourceState Fresh() => SourceState.Start("prometheus:slo_burn_rate", "prometheus", Now, TimeSpan.FromMinutes(2));

    [Fact]
    public void A_source_seen_for_the_first_time_starts_healthy_and_its_lookback_in_the_past()
    {
        SourceState state = Fresh();

        state.Health.ShouldBe(SourceHealth.Healthy);
        state.Checkpoint.ShouldBe(Now.AddMinutes(-2));
        state.Watermark.ShouldBe(Now.AddMinutes(-2));
    }

    [Fact]
    public void Advancing_moves_the_checkpoint_and_watermark_forward_but_never_back()
    {
        SourceState advanced = Fresh().Advance(Now.AddSeconds(-10), Now);
        SourceState repeated = advanced.Advance(Now.AddSeconds(-60), Now.AddSeconds(5));

        advanced.Checkpoint.ShouldBe(Now.AddSeconds(-10));
        advanced.Watermark.ShouldBe(Now.AddSeconds(-10));
        repeated.Checkpoint.ShouldBe(Now.AddSeconds(-10));
        repeated.Watermark.ShouldBe(Now.AddSeconds(-10));
        repeated.UpdatedAt.ShouldBe(Now.AddSeconds(5));
    }

    [Fact]
    public void Failures_degrade_a_source_and_the_third_in_a_row_makes_it_unavailable_without_moving_the_checkpoint()
    {
        SourceState once = Fresh().MarkFailed("connection refused", Now);
        SourceState twice = once.MarkFailed("connection refused", Now.AddSeconds(5));
        SourceState thrice = twice.MarkFailed("timeout", Now.AddSeconds(15));

        once.Health.ShouldBe(SourceHealth.Degraded);
        twice.Health.ShouldBe(SourceHealth.Degraded);
        thrice.Health.ShouldBe(SourceHealth.Unavailable);
        thrice.ConsecutiveFailures.ShouldBe(SourceState.UnavailableAfter);
        thrice.LastError.ShouldBe("timeout");
        thrice.Checkpoint.ShouldBe(Fresh().Checkpoint);
    }

    [Fact]
    public void A_successful_pull_recovers_an_unavailable_source_and_clears_its_failures()
    {
        SourceState unavailable = Fresh().MarkFailed("down", Now).MarkFailed("down", Now).MarkFailed("down", Now);

        SourceState recovered = unavailable.Advance(Now.AddSeconds(-5), Now.AddMinutes(1));

        recovered.Health.ShouldBe(SourceHealth.Healthy);
        recovered.ConsecutiveFailures.ShouldBe(0);
        recovered.LastError.ShouldBeNull();
        recovered.IsCoolingDown(Now.AddMinutes(1)).ShouldBeFalse();
    }

    [Fact]
    public void A_repeatedly_failing_source_backs_off_up_to_the_maximum()
    {
        SourceState state = Fresh();
        for (int failure = 0; failure < 10; failure++)
        {
            state = state.MarkFailed("down", Now);
        }

        state.NextAttemptAt.ShouldBe(Now + SourceState.MaxBackoff);
        state.IsCoolingDown(Now.AddMinutes(1)).ShouldBeTrue();
        Fresh().MarkFailed("down", Now).IsCoolingDown(Now).ShouldBeFalse();
    }

    [Theory]
    [InlineData(SourceHealth.Healthy, SourceHealth.Degraded, true)]
    [InlineData(SourceHealth.Healthy, SourceHealth.Unavailable, false)]
    [InlineData(SourceHealth.Degraded, SourceHealth.Unavailable, true)]
    [InlineData(SourceHealth.Degraded, SourceHealth.Healthy, true)]
    [InlineData(SourceHealth.Unavailable, SourceHealth.Healthy, true)]
    [InlineData(SourceHealth.Unavailable, SourceHealth.Degraded, false)]
    public void Health_moves_only_as_the_table_allows(SourceHealth from, SourceHealth to, bool allowed) =>
        SourceHealthTransitions.CanMove(from, to).ShouldBe(allowed);

    [Fact]
    public void A_stored_state_outside_the_table_is_refused_rather_than_silently_repaired()
    {
        SourceState corrupted = Fresh() with { ConsecutiveFailures = 2 };

        Should.Throw<InvalidOperationException>(() => corrupted.MarkFailed("down", Now));
    }
}
