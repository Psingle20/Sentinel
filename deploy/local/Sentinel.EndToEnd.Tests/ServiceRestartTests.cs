using System.Net.Http.Json;
using Reference.Scenarios.Application.Runs;
using Sentinel.Contracts.Incidents;
using Sentinel.Contracts.Messaging;
using Sentinel.Contracts.Remediation;
using Shouldly;
using Xunit.Abstractions;

namespace Sentinel.EndToEnd.Tests;

/// <summary>
/// Phase 01A: a single service restarting between steps repeats nothing. Runtime and Executor restart while the golden
/// incident's plan awaits approval, so the incident's grain, projection and workflow, and the service that holds the
/// rollback credential, all come back from their stores; the approval that follows still ends in one rollback and a
/// verified recovery, with one incident and one plan.
/// </summary>
[Collection(SystemCollection.Name)]
[Trait("Category", "EndToEnd")]
public sealed class ServiceRestartTests(SentinelSystem system, ITestOutputHelper output)
{
    private static readonly TimeSpan DecisionTimeout = TimeSpan.FromMinutes(8);
    private static readonly TimeSpan OutcomeTimeout = TimeSpan.FromMinutes(8);

    [Fact]
    public async Task S1_survives_Runtime_and_Executor_restarting_between_steps_and_repeats_nothing()
    {
        await system.WaitUntilQuietAsync(output);
        ScenarioRunView run = await system.StartScenarioAsync("S1");
        try
        {
            IncidentDetail incident = await system.WaitForIncidentAsync(detail => detail.Summary.Status == IncidentStatus.AwaitingApproval, DecisionTimeout, "S1 awaits approval");
            RemediationPlan plan = incident.Plan!.Plan;

            await system.RestartAsync("runtime-service");
            await system.RestartAsync("executor-service");
            output.WriteLine("Runtime and Executor restarted with the plan awaiting approval.");

            IncidentDetail restarted = await system.WaitForIncidentAsync(
                detail => detail.Summary.IncidentId == incident.Summary.IncidentId,
                DecisionTimeout,
                "the incident is served again after the restarts");
            restarted.Summary.Status.ShouldBe(IncidentStatus.AwaitingApproval, "a restart neither advances nor reopens the incident");
            restarted.Plan!.Plan.PlanVersion.ShouldBe(plan.PlanVersion);

            using var approve = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/incidents/{incident.Summary.IncidentId}/plans/{plan.PlanId}/approve")
            {
                Content = JsonContent.Create(new { planVersion = plan.PlanVersion, planHash = plan.PlanHash }, options: SentinelJson.Options)
            };
            approve.Headers.Add("Idempotency-Key", $"approve-{plan.PlanId}-v{plan.PlanVersion}-restarts");
            using HttpResponseMessage approval = await system.Api.SendAsync(approve);
            approval.EnsureSuccessStatusCode();

            IncidentDetail resolved = await system.WaitForIncidentAsync(
                detail => detail.Summary.IncidentId == incident.Summary.IncidentId && detail.Summary.Status is IncidentStatus.Resolved or IncidentStatus.Escalated,
                OutcomeTimeout,
                "S1 after the restarts is judged");
            output.WriteLine($"After the restarts: {resolved.Summary.Status}; execution {resolved.Execution?.Outcome}; recovery {resolved.Recovery?.Outcome}");

            resolved.Summary.Status.ShouldBe(IncidentStatus.Resolved);
            resolved.Execution.ShouldNotBeNull().Outcome.ShouldBe(ExecutionOutcome.ActionVerified);
            (await system.SentinelRollbacksAsync("checkout-service")).ShouldBe(1, "the rollback ran exactly once across both restarts");
            resolved.Timeline.Count(entry => entry.Kind == "plan.proposed").ShouldBe(1, "no plan was proposed again");
            (await system.IncidentsAsync()).Count(summary => summary.OpenedAt >= system.ScenarioStartedAt && summary.AffectedServiceId == incident.Summary.AffectedServiceId)
                .ShouldBe(1, "no second incident was opened for the same symptom");
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
}
