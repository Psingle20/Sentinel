using System.Net.Http.Json;
using Reference.Scenarios.Application.Runs;
using Sentinel.Contracts.Incidents;
using Sentinel.Contracts.Messaging;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Contracts.Remediation;
using Shouldly;
using Xunit.Abstractions;

namespace Sentinel.EndToEnd.Tests;

/// <summary>
/// Phase E01, Stage 2: Sentinel against the AWS environment. <c>AWS-S1</c> is S6 on SQS and ECS: Sentinel reads
/// the innocent ECS revision as a change through its ECS provider, blames the poison message instead, quarantines it
/// after approval through the queue's redrive policy, and verifies from the consumer's telemetry that the queue recovers.
/// <c>AWS-S7</c> (Stage 4) drifts the EventBridge rule that feeds the queue after an innocent ECS revision is serving:
/// Sentinel reads the rule's change from EventBridge, blames it rather than the revision, restores the rule's approved
/// pattern after approval and verifies that orders arrive again. The deploy-blame baseline would roll the revision back in
/// both.
/// </summary>
[Collection(AwsSystemCollection.Name)]
[Trait("Category", "EndToEnd")]
[Trait("Suite", "Aws")]
public sealed class AwsScenarioTests(AwsSystem system, ITestOutputHelper output)
{
    private static readonly TimeSpan DetectionTimeout = TimeSpan.FromMinutes(7);
    private static readonly TimeSpan RecoveryTimeout = TimeSpan.FromMinutes(7);

    [Fact]
    public async Task AWS_S1_a_poison_message_on_sqs_is_quarantined_not_rolled_back_and_the_queue_recovers()
    {
        await system.WaitUntilQuietAsync(output);
        ScenarioRunView run = await system.StartScenarioAsync("AWS-S1");
        try
        {
            IncidentDetail incident = await system.WaitForIncidentAsync(
                detail => detail.Summary.AffectedServiceId.Value == "queue-consumer" && detail.Decision is not null && detail.Summary.Status is IncidentStatus.AwaitingApproval or IncidentStatus.Escalated,
                DetectionTimeout,
                "AWS-S1 is decided");
            Log(incident);

            // The revision starts serving after onset, so correlation does not link it; it reaches the investigation as a
            // change read from ECS, by its image tag, and the verifier eliminates it. An earlier scenario's rollout can still
            // be inside the lookback, so every ECS revision is a rival, and each one loses.
            HypothesisView[] revisions = [.. incident.Hypotheses.Where(hypothesis => hypothesis.Finding.SuspectedChangeObservationId is { } observation && observation.Value.StartsWith("ecs:deployment:", StringComparison.Ordinal))];
            revisions.ShouldNotBeEmpty("the ECS revision reaches Sentinel as a change");
            revisions.ShouldAllBe(revision => revision.Finding.Statement.Contains("v1.4.1") && revision.Status == HypothesisStatus.Rejected);
            incident.Summary.Status.ShouldBe(IncidentStatus.AwaitingApproval);
            QuarantineMessageAction quarantine = incident.Plan.ShouldNotBeNull().Plan.Actions.Single().ShouldBeOfType<QuarantineMessageAction>();
            quarantine.Queue.ShouldBe("orders");
            incident.Plan.PolicyEvaluation.Decision.ShouldBe(PolicyDecision.ApprovalRequired);

            RemediationPlan plan = incident.Plan.Plan;
            using var approve = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/incidents/{incident.Summary.IncidentId}/plans/{plan.PlanId}/approve")
            {
                Content = JsonContent.Create(new { planVersion = plan.PlanVersion, planHash = plan.PlanHash }, options: SentinelJson.Options)
            };
            approve.Headers.Add("Idempotency-Key", $"approve-{plan.PlanId}-v{plan.PlanVersion}");
            using HttpResponseMessage approval = await system.Api.SendAsync(approve);
            approval.EnsureSuccessStatusCode();

            IncidentDetail resolved = await system.WaitForIncidentAsync(
                detail => detail.Summary.IncidentId == incident.Summary.IncidentId && detail.Summary.Status is IncidentStatus.Resolved or IncidentStatus.Escalated,
                RecoveryTimeout,
                "AWS-S1 recovery is judged");
            Log(resolved);

            resolved.Summary.Status.ShouldBe(IncidentStatus.Resolved);
            resolved.Execution.ShouldNotBeNull().Outcome.ShouldBe(ExecutionOutcome.ActionVerified);
            resolved.Execution.Detail.ShouldNotBeNull().ShouldContain("Dead-lettered", customMessage: "the quarantine went through SQS");
            resolved.Recovery.ShouldNotBeNull().Outcome.ShouldBe(RecoveryOutcome.Recovered);

            await system.RecordOutcomeAsync("AWS-S1", resolved, approved: true);
            await EvaluationRun.WriteAsync(system, "AWS-S1", resolved, approved: true);
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

    [Fact]
    public async Task AWS_S7_a_drifted_event_route_is_restored_not_rolled_back_and_orders_arrive_again()
    {
        await system.WaitUntilQuietAsync(output);
        ScenarioRunView run = await system.StartScenarioAsync("AWS-S7");
        try
        {
            IncidentDetail incident = await system.WaitForIncidentAsync(
                detail => detail.Summary.AffectedServiceId.Value == "queue-consumer" && detail.Summary.Title.Contains("orders_undelivered_rate", StringComparison.Ordinal)
                    && detail.Decision is not null && detail.Summary.Status is IncidentStatus.AwaitingApproval or IncidentStatus.Escalated,
                DetectionTimeout,
                "AWS-S7 is decided");
            Log(incident);

            // The rule's change is read from EventBridge, linked to the incident, and leads; the serving revision does not. An
            // earlier scenario's changes can still be inside the lookback, so the assertions name the leader, not a count.
            HypothesisView leader = incident.Hypotheses.Single(hypothesis => hypothesis.Finding.HypothesisId == incident.Decision!.LeadingHypothesisId);
            ObservationId routeChange = leader.Finding.SuspectedChangeObservationId.ShouldNotBeNull();
            routeChange.Value.ShouldStartWith("eventbridge:deployment:", Case.Sensitive, "the route change leads");
            LinkedChangeView route = incident.Changes.Single(change => change.ObservationIds.Contains(routeChange));
            route.ChangeKind.ShouldBe("route", "the drifted rule reaches Sentinel as a change, linked to the incident");
            route.ServiceId.Value.ShouldBe("queue-consumer");
            HypothesisView[] revisions = [.. incident.Hypotheses.Where(hypothesis => hypothesis.Finding.SuspectedChangeObservationId is { } observation && observation.Value.StartsWith("ecs:deployment:", StringComparison.Ordinal))];
            revisions.ShouldNotBeEmpty("the serving ECS revision is a rival explanation");
            revisions.ShouldAllBe(revision => revision.Status == HypothesisStatus.Rejected || revision.Status == HypothesisStatus.Challenged);
            incident.Summary.Status.ShouldBe(IncidentStatus.AwaitingApproval);
            RestoreRouteAction restore = incident.Plan.ShouldNotBeNull().Plan.Actions.Single().ShouldBeOfType<RestoreRouteAction>();
            restore.Rule.ShouldBe("orders-route");
            incident.Plan.PolicyEvaluation.Decision.ShouldBe(PolicyDecision.ApprovalRequired);

            RemediationPlan plan = incident.Plan.Plan;
            using var approve = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/incidents/{incident.Summary.IncidentId}/plans/{plan.PlanId}/approve")
            {
                Content = JsonContent.Create(new { planVersion = plan.PlanVersion, planHash = plan.PlanHash }, options: SentinelJson.Options)
            };
            approve.Headers.Add("Idempotency-Key", $"approve-{plan.PlanId}-v{plan.PlanVersion}");
            using HttpResponseMessage approval = await system.Api.SendAsync(approve);
            approval.EnsureSuccessStatusCode();

            IncidentDetail resolved = await system.WaitForIncidentAsync(
                detail => detail.Summary.IncidentId == incident.Summary.IncidentId && detail.Summary.Status is IncidentStatus.Resolved or IncidentStatus.Escalated,
                RecoveryTimeout,
                "AWS-S7 recovery is judged");
            Log(resolved);

            resolved.Summary.Status.ShouldBe(IncidentStatus.Resolved);
            resolved.Execution.ShouldNotBeNull().Outcome.ShouldBe(ExecutionOutcome.ActionVerified);
            resolved.Execution.Detail.ShouldNotBeNull().ShouldContain("Restored rule orders-route", customMessage: "the route was restored through EventBridge");
            resolved.Recovery.ShouldNotBeNull().Outcome.ShouldBe(RecoveryOutcome.Recovered);

            await system.RecordOutcomeAsync("AWS-S7", resolved, approved: true);
            await EvaluationRun.WriteAsync(system, "AWS-S7", resolved, approved: true);
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

    private void Log(IncidentDetail incident)
    {
        output.WriteLine($"{incident.Summary.Title}: {incident.Summary.Status}");
        foreach (TimelineEntry entry in incident.Timeline)
        {
            output.WriteLine($"  v{entry.StreamVersion} {entry.OccurredAt:HH:mm:ss} {entry.Summary}");
        }
    }
}
