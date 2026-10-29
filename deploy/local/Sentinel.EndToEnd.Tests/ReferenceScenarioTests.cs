using System.Net.Http.Json;
using Reference.Scenarios.Api.Contracts;
using Reference.Scenarios.Api.Manifests;
using Reference.Scenarios.Application.Runs;
using Reference.Scenarios.Domain.Scenarios;
using Shouldly;
using Xunit.Abstractions;

namespace Sentinel.EndToEnd.Tests;

/// <summary>
/// Phase 01 exit criteria, checked against the reference Prometheus with Sentinel not running:
/// each scenario's symptoms and discriminating signals appear within bounded time, the permitted
/// remediation restores the recovery signals, and S0 never burns the SLO.
/// </summary>
[Collection(ReferenceCollection.Name)]
[Trait("Category", "EndToEnd")]
public sealed class ReferenceScenarioTests(ReferenceOnlySystem system, ITestOutputHelper output)
{
    [Fact]
    public async Task The_healthy_system_serves_checkout_traffic_without_burning_its_slo()
    {
        double rate = await Eventually.ValueAsync(
            async () => await system.Prometheus.ScalarAsync("sum(rate(reference_checkout_requests_total[1m]))") ?? 0,
            value => value > 5,
            TimeSpan.FromMinutes(2),
            "checkout traffic reaches Prometheus");

        rate.ShouldBeLessThan(15);
        (await system.Prometheus.ScalarAsync("slo:checkout_success:burn_rate1m") ?? 0).ShouldBeLessThan(1);
    }

    [Theory]
    [InlineData("S1")]
    [InlineData("S2")]
    [InlineData("S3")]
    public async Task A_scenario_shows_its_symptoms_and_discriminating_signals_and_the_permitted_remediation_recovers(string scenarioId)
    {
        ScenarioManifest manifest = system.Scenarios.Manifests[scenarioId];
        ScenarioRunView run = await system.StartScenarioAsync(scenarioId);
        try
        {
            DateTimeOffset activatedAt = run.ActivatedAt!.Value;

            SignalExpectation[] signals = [.. manifest.ExpectedObservations, .. manifest.DiscriminatingSignals];
            await Task.WhenAll(signals.Select(signal => AssertSignalAsync(signal, activatedAt, manifest.Noise.TimingToleranceSeconds)));

            await RemediateAsync(manifest, run.RunId);
            await AssertRecoveryAsync(manifest.Recovery!);

            ScenarioRunView finished = await system.WaitForRunToFinishAsync(run.RunId, TimeSpan.FromSeconds(manifest.DurationSeconds + 60));
            finished.Steps.ShouldAllBe(step => step.Error == null);
        }
        catch
        {
            await system.ReportAsync(output, run.RunId);
            throw;
        }
        finally
        {
            await system.StopScenarioAsync(run.RunId);
        }
    }

    private async Task AssertSignalAsync(SignalExpectation signal, DateTimeOffset activatedAt, int toleranceSeconds)
    {
        DateTimeOffset from = activatedAt.AddSeconds(signal.FromSeconds);
        DateTimeOffset deadline = activatedAt.AddSeconds(signal.WithinSeconds + toleranceSeconds);
        while (DateTimeOffset.UtcNow < from)
        {
            await Task.Delay(TimeSpan.FromSeconds(1));
        }

        while (true)
        {
            double value = await system.Prometheus.ScalarAsync(signal.Query) ?? 0;
            bool met = signal.Expectation == "above" ? value > signal.Threshold : value < signal.Threshold;
            double elapsed = (DateTimeOffset.UtcNow - activatedAt).TotalSeconds;

            if (signal.Expectation == "above" && met)
            {
                output.WriteLine($"+{elapsed:F0}s  {signal.Description}: {value:G4} > {signal.Threshold}");
                return;
            }

            if (signal.Expectation == "below" && !met)
            {
                throw new ShouldAssertException($"{signal.Description}: {value:G4} reached {signal.Threshold} at +{elapsed:F0}s.");
            }

            if (DateTimeOffset.UtcNow > deadline)
            {
                if (signal.Expectation == "below")
                {
                    output.WriteLine($"+{elapsed:F0}s  {signal.Description}: stayed below {signal.Threshold}");
                    return;
                }

                throw new ShouldAssertException($"{signal.Description}: {value:G4} did not exceed {signal.Threshold} by +{signal.WithinSeconds + toleranceSeconds}s.");
            }

            await Task.Delay(TimeSpan.FromSeconds(2));
        }
    }

    private async Task RemediateAsync(ScenarioManifest manifest, string runId)
    {
        RemediationExpectation remedy = manifest.Remediation.Permitted.Single();
        if (remedy.Action == "pause-batch")
        {
            (await system.ScenarioRunner.PostAsync("/api/actions/stop-batch", null)).EnsureSuccessStatusCode();
            return;
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/services/{remedy.Service}/deployments")
        {
            Content = JsonContent.Create(new { version = remedy.ToVersion, kind = "rollback", actor = "end-to-end-test", reason = runId })
        };
        request.Headers.Add("Idempotency-Key", $"{runId}:remediation");
        (await system.DeployController.SendAsync(request)).EnsureSuccessStatusCode();

        await Eventually.ValueAsync(
            async () => (await system.DeployController.GetFromJsonAsync<ServiceStatus>($"/api/services/{remedy.Service}"))!,
            status => status.ServingRevision == remedy.ToVersion,
            TimeSpan.FromMinutes(1),
            $"{remedy.Service} serves {remedy.ToVersion}");
    }

    private async Task AssertRecoveryAsync(ScenarioRecovery recovery)
    {
        await Eventually.ValueAsync(
            () => AllMetAsync(recovery.Criteria),
            met => met,
            TimeSpan.FromSeconds(recovery.EvaluationTimeoutSeconds),
            "every recovery criterion is met",
            TimeSpan.FromSeconds(5));

        // Recovered means the criteria hold for the whole observation window, not just once.
        DateTimeOffset windowEnd = DateTimeOffset.UtcNow.AddSeconds(recovery.ObservationWindowSeconds);
        while (DateTimeOffset.UtcNow < windowEnd)
        {
            (await AllMetAsync(recovery.Criteria)).ShouldBeTrue("a recovery criterion regressed inside the observation window");
            await Task.Delay(TimeSpan.FromSeconds(10));
        }
    }

    private async Task<bool> AllMetAsync(IReadOnlyList<RecoveryExpectation> criteria)
    {
        foreach (RecoveryExpectation criterion in criteria)
        {
            double value = await system.Prometheus.ScalarAsync(criterion.Query) ?? 0;
            bool met = criterion.Comparison switch
            {
                "lessThan" => value < criterion.Threshold,
                "lessThanOrEqual" => value <= criterion.Threshold,
                "greaterThan" => value > criterion.Threshold,
                _ => value >= criterion.Threshold
            };
            if (!met)
            {
                return false;
            }
        }

        return true;
    }

    private sealed record ServiceStatus(string Name, string? ServingRevision, string BaselineRevision);
}
