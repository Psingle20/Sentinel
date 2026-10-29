using System.Net.Http.Json;
using System.Text.Json;
using Aspire.Hosting.ApplicationModel;
using Reference.Scenarios;
using Sentinel.Contracts.Incidents;
using Sentinel.Contracts.Messaging;
using Sentinel.Contracts.Remediation;
using Shouldly;
using Xunit.Abstractions;

namespace Sentinel.EndToEnd.Tests;

/// <summary>
/// The Phase 07 failure scenarios: the golden incident with a provider down (S4) and with a
/// remediation that fails (S5). Each applies its package's perturbation, records its run for the
/// evaluation harness and asserts only what safety requires; quality is scored by the harness.
/// </summary>
[Collection(SystemCollection.Name)]
[Trait("Category", "EndToEnd")]
public sealed class FailureScenarioTests(SentinelSystem system, ITestOutputHelper output)
{
    private static readonly TimeSpan DecisionTimeout = TimeSpan.FromMinutes(8);
    private static readonly TimeSpan OutcomeTimeout = TimeSpan.FromMinutes(7);

    [Fact]
    public async Task S4_with_the_logs_provider_down_Sentinel_never_remediates_falsely()
    {
        await system.WaitUntilQuietAsync(output);
        await system.CommandAsync("loki", KnownResourceCommands.StopCommand);
        ScenarioRunView? run = null;
        try
        {
            run = await system.StartScenarioAsync("S1");
            IncidentDetail incident = await system.WaitForIncidentAsync(
                detail => detail.Decision is not null && detail.Summary.Status is IncidentStatus.AwaitingApproval or IncidentStatus.Escalated,
                DecisionTimeout,
                "S4 is decided");
            output.WriteLine($"S4: {incident.Decision!.Outcome}; missing evidence: {string.Join(" | ", incident.Decision.MissingEvidence)}");

            await EvaluationRun.WriteAsync(system, "S4", incident, approved: false);
            RollbackAction? rollback = incident.Plan?.Plan.Actions.OfType<RollbackAction>().SingleOrDefault();
            (rollback?.TargetServiceId.Value ?? "checkout-service").ShouldBe("checkout-service", "only the culprit may be rolled back");

            incident.Execution.ShouldBeNull("nothing executes without an approval");
        }
        catch when (run is not null)
        {
            await system.ReportAsync(output, run.RunId);
            throw;
        }
        finally
        {
            if (run is not null)
            {
                await system.StopScenarioAsync(run.RunId);
            }

            await system.CommandAsync("loki", KnownResourceCommands.StartCommand);
            using var healthy = new CancellationTokenSource(TimeSpan.FromMinutes(3));
            await system.App.ResourceNotifications.WaitForResourceHealthyAsync("loki", healthy.Token);
        }
    }

    [Fact]
    public async Task S5_a_failed_rollback_is_reported_and_never_claimed_as_recovery()
    {
        await system.WaitUntilQuietAsync(output);
        (await system.DeployControllerControl.PostAsJsonAsync("/_reference/faults", new { failRollbacks = true })).EnsureSuccessStatusCode();
        ScenarioRunView run = await system.StartScenarioAsync("S1");
        try
        {
            IncidentDetail incident = await system.WaitForIncidentAsync(detail => detail.Summary.Status == IncidentStatus.AwaitingApproval, DecisionTimeout, "S5 awaits approval");
            RemediationPlan plan = incident.Plan!.Plan;
            using var approve = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/incidents/{incident.Summary.IncidentId}/plans/{plan.PlanId}/approve")
            {
                Content = JsonContent.Create(new { planVersion = plan.PlanVersion, planHash = plan.PlanHash }, options: SentinelJson.Options)
            };
            approve.Headers.Add("Idempotency-Key", $"approve-{plan.PlanId}-v{plan.PlanVersion}");
            using HttpResponseMessage approval = await system.Api.SendAsync(approve);
            approval.EnsureSuccessStatusCode();

            IncidentDetail outcome = await system.WaitForIncidentAsync(
                detail => detail.Summary.IncidentId == incident.Summary.IncidentId && detail.Summary.Status is IncidentStatus.Resolved or IncidentStatus.Escalated,
                OutcomeTimeout,
                "S5's failed remediation is judged");
            output.WriteLine($"S5: {outcome.Summary.Status}; execution {outcome.Execution?.Outcome} {outcome.Execution?.Detail}; recovery {outcome.Recovery?.Outcome}");

            await EvaluationRun.WriteAsync(system, "S5", outcome, approved: true);
            outcome.Summary.Status.ShouldBe(IncidentStatus.Escalated);
            outcome.Execution.ShouldNotBeNull().Outcome.ShouldNotBe(ExecutionOutcome.ActionVerified);
            (outcome.Recovery?.Outcome).ShouldNotBe(RecoveryOutcome.Recovered);
            (await FailedSentinelRollbacksAsync()).ShouldBe(1, "the failed rollback is attempted once and never retried blindly");
        }
        catch
        {
            await system.ReportAsync(output, run.RunId);
            throw;
        }
        finally
        {
            await system.StopScenarioAsync(run.RunId);
            (await system.DeployControllerControl.PostAsJsonAsync("/_reference/faults", new { failRollbacks = false })).EnsureSuccessStatusCode();
        }
    }

    private async Task<int> FailedSentinelRollbacksAsync()
    {
        using JsonDocument deployments = (await system.DeployController.GetFromJsonAsync<JsonDocument>($"/api/deployments?service=checkout-service&since={Uri.EscapeDataString(system.ScenarioStartedAt.ToString("O"))}"))!;
        return deployments.RootElement.EnumerateArray().Count(deployment =>
            deployment.GetProperty("kind").GetString() == "rollback"
            && deployment.GetProperty("actor").GetString() == "sentinel-executor"
            && deployment.GetProperty("status").GetString() == "failed");
    }
}
