using System.Net.Http.Json;
using Reference.Scenarios.Application.Runs;
using Sentinel.Contracts.Incidents;
using Sentinel.Contracts.Messaging;
using Sentinel.Contracts.Remediation;
using Shouldly;
using Xunit.Abstractions;

namespace Sentinel.EndToEnd.Tests;

/// <summary>
/// Phase 09 failure injection: the messaging Redis stops answering while the golden plan is approved, as a partitioned
/// broker would, and comes back with its data. The Gateway still accepts the approval, because it writes it to its
/// outbox; once the broker answers again every service catches up, messages sent twice are handled once, and the
/// incident ends in one rollback and a verified recovery.
/// </summary>
[Collection(SystemCollection.Name)]
[Trait("Category", "EndToEnd")]
public sealed class MessagingOutageTests(SentinelSystem system, ITestOutputHelper output)
{
    private const string Messaging = "messaging";
    private static readonly TimeSpan DecisionTimeout = TimeSpan.FromMinutes(8);
    private static readonly TimeSpan OutcomeTimeout = TimeSpan.FromMinutes(8);
    private static readonly TimeSpan Outage = TimeSpan.FromSeconds(45);

    [Fact]
    public async Task S1_survives_the_message_broker_going_silent_while_its_plan_is_approved()
    {
        await system.WaitUntilQuietAsync(output);
        ScenarioRunView run = await system.StartScenarioAsync("S1");
        try
        {
            IncidentDetail incident = await system.WaitForIncidentAsync(detail => detail.Summary.Status == IncidentStatus.AwaitingApproval, DecisionTimeout, "S1 awaits approval");
            RemediationPlan plan = incident.Plan!.Plan;

            await system.PauseAsync(Messaging);
            try
            {
                using var approve = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/incidents/{incident.Summary.IncidentId}/plans/{plan.PlanId}/approve")
                {
                    Content = JsonContent.Create(new { planVersion = plan.PlanVersion, planHash = plan.PlanHash }, options: SentinelJson.Options)
                };
                approve.Headers.Add("Idempotency-Key", $"approve-{plan.PlanId}-v{plan.PlanVersion}-outage");
                using HttpResponseMessage approval = await system.Api.SendAsync(approve);
                approval.EnsureSuccessStatusCode();
                output.WriteLine($"Approved with {Messaging} paused.");

                await Task.Delay(Outage);
                IncidentDetail waiting = await system.WaitForIncidentAsync(detail => detail.Summary.IncidentId == incident.Summary.IncidentId, OutcomeTimeout, "the incident is served during the outage");
                waiting.Summary.Status.ShouldBe(IncidentStatus.AwaitingApproval, "the approval travels as a message, so it cannot arrive while the broker is silent");
            }
            finally
            {
                await system.UnpauseAsync(Messaging);
                output.WriteLine($"{Messaging} answers again after {Outage.TotalSeconds:F0} s.");
            }

            IncidentDetail resolved = await system.WaitForIncidentAsync(
                detail => detail.Summary.IncidentId == incident.Summary.IncidentId && detail.Summary.Status is IncidentStatus.Resolved or IncidentStatus.Escalated,
                OutcomeTimeout,
                "S1 after the outage is judged");
            output.WriteLine($"After the outage: {resolved.Summary.Status}; execution {resolved.Execution?.Outcome}; recovery {resolved.Recovery?.Outcome}");

            resolved.Summary.Status.ShouldBe(IncidentStatus.Resolved);
            resolved.Execution.ShouldNotBeNull().Outcome.ShouldBe(ExecutionOutcome.ActionVerified);
            (await system.SentinelRollbacksAsync("checkout-service")).ShouldBe(1, "the rollback ran exactly once although messages were sent again");
            resolved.Timeline.Count(entry => entry.Kind == "plan.approved").ShouldBe(1, "the approval was recorded once");
            (await system.IncidentsAsync()).Count(summary => summary.OpenedAt >= system.ScenarioStartedAt && summary.AffectedServiceId == incident.Summary.AffectedServiceId)
                .ShouldBe(1, "observations delivered late or twice opened no second incident");
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
