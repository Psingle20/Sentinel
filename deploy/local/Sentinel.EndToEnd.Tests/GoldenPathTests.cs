using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Reference.Scenarios.Api.Contracts;
using Reference.Scenarios.Api.Manifests;
using Reference.Scenarios.Application.Runs;
using Reference.Scenarios.Domain.Scenarios;
using Sentinel.Contracts.Incidents;
using Sentinel.Contracts.Investigations;
using Sentinel.Contracts.Messaging;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Contracts.Remediation;
using Shouldly;
using Xunit.Abstractions;

namespace Sentinel.EndToEnd.Tests;

/// <summary>
/// The Phase 01A walking skeleton, end to end across real process boundaries: the reference
/// system breaks, Ingestion observes it, Runtime correlates and decides, AgentWorker investigates
/// with the model test double, a responder reviews the diagnosis and approves in Sentinel.Web,
/// Executor rolls back through the deploy controller, and Runtime verifies recovery from telemetry.
/// The responder then reviews the recovery, the postmortem and a replay in Sentinel.Web, so the
/// responder journeys of Phase 08, one to six, run against the real system.
/// </summary>
[Collection(SystemCollection.Name)]
[Trait("Category", "EndToEnd")]
public sealed class GoldenPathTests(SentinelSystem system, ITestOutputHelper output)
{
    private static readonly TimeSpan DetectionTimeout = TimeSpan.FromMinutes(7);
    private static readonly TimeSpan RecoveryTimeout = TimeSpan.FromMinutes(7);

    [Fact]
    public async Task S1_runs_from_break_to_approved_rollback_and_verified_recovery()
    {
        await system.WaitUntilQuietAsync(output);
        ScenarioRunView run = await system.StartScenarioAsync("S1");
        try
        {
            IncidentDetail incident = await system.WaitForIncidentAsync(
                detail => detail.Decision is not null && detail.Summary.Status is IncidentStatus.AwaitingApproval or IncidentStatus.Escalated,
                DetectionTimeout,
                "S1 is decided");
            Log(incident);
            if (incident.Summary.Status == IncidentStatus.Escalated)
            {
                // A live model can abstain or blame the wrong change; that run is the one most worth scoring, so it is
                // recorded before the assertions below fail it.
                await EvaluationRun.WriteAsync(system, "S1", incident, approved: false);
            }

            incident.Summary.Status.ShouldBe(IncidentStatus.AwaitingApproval);

            incident.Decision!.Outcome.ShouldBe(DecisionOutcome.Diagnosed);
            incident.Decision.Checks.ShouldAllBe(check => check.Passed);
            RollbackAction rollback = incident.Plan!.Plan.Actions.OfType<RollbackAction>().Single();
            rollback.TargetServiceId.Value.ShouldBe("checkout-service");
            rollback.FromRevision.ShouldBe("v2.4.1");
            rollback.ToRevision.ShouldBe("v2.4.0");
            incident.Plan.PolicyEvaluation.Decision.ShouldBe(PolicyDecision.ApprovalRequired);
            await system.ShouldHaveEverySignalAsync(output);

            await ApproveInBrowserAsync(incident);

            IncidentDetail resolved = await system.WaitForIncidentAsync(detail => detail.Summary.IncidentId == incident.Summary.IncidentId && detail.Summary.Status is IncidentStatus.Resolved or IncidentStatus.Escalated, RecoveryTimeout, "S1 recovery is judged");
            Log(resolved);

            resolved.Summary.Status.ShouldBe(IncidentStatus.Resolved);
            resolved.Execution!.Outcome.ShouldBe(ExecutionOutcome.ActionVerified);
            resolved.Recovery!.Outcome.ShouldBe(RecoveryOutcome.Recovered);
            (await system.SentinelRollbacksAsync("checkout-service")).ShouldBe(1, "the rollback ran exactly once");

            await system.RecordOutcomeAsync("S1", resolved, approved: true);
            await EvaluationRun.WriteAsync(system, "S1", resolved, approved: true);

            await ReviewInBrowserAsync(resolved);
            await ShouldReplayWithoutProvidersOrActionsAsync(resolved);
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

    [Theory]
    [InlineData("S2")]
    [InlineData("S3")]
    public async Task A_confounder_runs_through_decision_and_plan_without_approval_and_its_outcome_is_recorded(string scenarioId)
    {
        await system.WaitUntilQuietAsync(output);
        ScenarioRunView run = await system.StartScenarioAsync(scenarioId);
        try
        {
            IncidentDetail incident = await system.WaitForIncidentAsync(
                detail => detail.Decision is not null && detail.Summary.Status is IncidentStatus.AwaitingApproval or IncidentStatus.Escalated,
                DetectionTimeout,
                $"{scenarioId} is decided");
            Log(incident);

            // No scripted approval (Phase 01A exit criteria): the outcome is recorded, not asserted.
            await system.RecordOutcomeAsync(scenarioId, incident, approved: false);
            await EvaluationRun.WriteAsync(system, scenarioId, incident, approved: false);
            incident.Execution.ShouldBeNull("nothing executes without an approval");
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

    /// <summary>
    /// S6 (Phase E01): a poison message keeps failing on every receive, and a benign consumer deployment serves before
    /// the incident opens. Sentinel blames the message, not the deployment, quarantines it after approval and verifies
    /// that the queue recovers; nothing rolls the consumer back, which is what the deploy-blame baseline would do.
    /// </summary>
    [Fact]
    public async Task S6_a_poison_message_is_quarantined_not_rolled_back_and_the_queue_recovers()
    {
        await system.WaitUntilQuietAsync(output);
        ScenarioRunView run = await system.StartScenarioAsync("S6");
        try
        {
            IncidentDetail incident = await system.WaitForIncidentAsync(
                detail => detail.Summary.AffectedServiceId.Value == "queue-consumer" && detail.Decision is not null && detail.Summary.Status is IncidentStatus.AwaitingApproval or IncidentStatus.Escalated,
                DetectionTimeout,
                "S6 is decided");
            Log(incident);

            incident.Summary.Status.ShouldBe(IncidentStatus.AwaitingApproval);
            QuarantineMessageAction quarantine = incident.Plan.ShouldNotBeNull().Plan.Actions.Single().ShouldBeOfType<QuarantineMessageAction>();
            quarantine.TargetServiceId.Value.ShouldBe("queue-consumer");
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
                "S6 recovery is judged");
            Log(resolved);

            resolved.Summary.Status.ShouldBe(IncidentStatus.Resolved);
            resolved.Execution.ShouldNotBeNull().Outcome.ShouldBe(ExecutionOutcome.ActionVerified);
            resolved.Recovery.ShouldNotBeNull().Outcome.ShouldBe(RecoveryOutcome.Recovered);
            (await system.SentinelRollbacksAsync("queue-consumer")).ShouldBe(0, "the consumer's deployment was innocent");

            await system.RecordOutcomeAsync("S6", resolved, approved: true);
            await EvaluationRun.WriteAsync(system, "S6", resolved, approved: true);
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

    /// <summary>
    /// S7 (Phase E01): order-service's confirmations slow down and the queue-consumer's immediate retries amplify it, and a
    /// benign consumer deployment serves before the incident opens. Sentinel blames order-service, not the consumer, and
    /// escalates, because no action it holds contains the retries; it neither rolls the consumer back nor quarantines
    /// its messages.
    /// </summary>
    [Fact]
    public async Task S7_retry_amplification_blames_the_slow_dependency_and_escalates_without_touching_the_consumer()
    {
        await system.WaitUntilQuietAsync(output);
        ScenarioRunView run = await system.StartScenarioAsync("S7");
        try
        {
            IncidentDetail incident = await system.WaitForIncidentAsync(
                detail => detail.Summary.AffectedServiceId.Value == "queue-consumer" && detail.Decision is not null && detail.Summary.Status is IncidentStatus.AwaitingApproval or IncidentStatus.Escalated or IncidentStatus.Resolved,
                DetectionTimeout,
                "S7 is decided");
            Log(incident);

            incident.Summary.Status.ShouldBe(IncidentStatus.Escalated);
            incident.Decision!.Outcome.ShouldBe(DecisionOutcome.Diagnosed);
            incident.Hypotheses.Single(hypothesis => hypothesis.Finding.HypothesisId == incident.Decision.LeadingHypothesisId).Finding.Key.ShouldBe("dependency-regression:order-service");
            incident.Plan.ShouldBeNull("no action Sentinel holds contains the consumer's retries");
            incident.Execution.ShouldBeNull();
            (await system.SentinelRollbacksAsync("queue-consumer")).ShouldBe(0, "the consumer's deployment was innocent");

            await system.RecordOutcomeAsync("S7", incident, approved: false);
            await EvaluationRun.WriteAsync(system, "S7", incident, approved: false);
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

    /// <summary>
    /// S8 (Phase E01): duplicate publications meet a booking that is not idempotent, and a benign consumer deployment
    /// serves before the incident opens. Sentinel diagnoses the duplicates and escalates, because no action it holds
    /// makes the consumer idempotent; nothing rolls the consumer back, which is what the deploy-blame baseline would do.
    /// </summary>
    [Fact]
    public async Task S8_duplicate_delivery_is_diagnosed_and_escalated_without_a_rollback()
    {
        await system.WaitUntilQuietAsync(output);
        ScenarioRunView run = await system.StartScenarioAsync("S8");
        try
        {
            IncidentDetail incident = await system.WaitForIncidentAsync(
                detail => detail.Summary.AffectedServiceId.Value == "queue-consumer" && detail.Decision is not null && detail.Summary.Status is IncidentStatus.AwaitingApproval or IncidentStatus.Escalated or IncidentStatus.Resolved,
                DetectionTimeout,
                "S8 is decided");
            Log(incident);

            incident.Summary.Status.ShouldBe(IncidentStatus.Escalated);
            incident.Decision!.Outcome.ShouldBe(DecisionOutcome.Diagnosed);
            incident.Hypotheses.Single(hypothesis => hypothesis.Finding.HypothesisId == incident.Decision.LeadingHypothesisId).Finding.Key.ShouldBe("duplicate-delivery:queue-consumer");
            incident.Plan.ShouldBeNull("no action Sentinel holds makes the consumer idempotent");
            incident.Execution.ShouldBeNull();
            (await system.SentinelRollbacksAsync("queue-consumer")).ShouldBe(0, "the consumer's deployment was innocent");

            await system.RecordOutcomeAsync("S8", incident, approved: false);
            await EvaluationRun.WriteAsync(system, "S8", incident, approved: false);
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

    /// <summary>S0, the healthy control: changes and noise without an SLO burn must open no incident.</summary>
    [Fact]
    public async Task S0_the_healthy_control_opens_no_incident()
    {
        await system.WaitUntilQuietAsync(output);
        ScenarioRunView run = await system.StartScenarioAsync("S0");
        try
        {
            await system.WaitForRunToFinishAsync(run.RunId, TimeSpan.FromMinutes(6));

            // Give detection the scenario's full window after the last change before concluding.
            await Task.Delay(TimeSpan.FromSeconds(60));
            IReadOnlyList<IncidentSummary> opened = [.. (await system.IncidentsAsync()).Where(incident => incident.OpenedAt >= system.ScenarioStartedAt)];
            output.WriteLine($"Incidents opened during S0: {opened.Count}");
            IncidentDetail? first = opened.Count == 0 ? null : await system.Api.GetFromJsonAsync<IncidentDetail>($"/api/v1/incidents/{opened[0].IncidentId}", SentinelJson.Options);
            await EvaluationRun.WriteAsync(system, "S0", first, approved: false);
            opened.ShouldBeEmpty("a healthy system with change noise is not an incident");
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

    /// <summary>
    /// Phase 07's first exit criterion: the golden incident replays through the gateway, at level 1 from its
    /// stream and at level 2 from every agent execution's captured inputs, without reaching a provider or
    /// executing anything.
    /// </summary>
    private async Task ShouldReplayWithoutProvidersOrActionsAsync(IncidentDetail resolved)
    {
        Guid incidentId = resolved.Summary.IncidentId.Value;
        DomainReplayResult domain = await PostAsync<DomainReplayResult>($"/api/v1/incidents/{incidentId}/replays/domain", null);
        output.WriteLine($"Domain replay: {domain.EventCount} events to v{domain.StreamVersion}, state {domain.StateHash[..12]}, {domain.Decisions.Count} decision(s)");
        domain.Level.ShouldBe(ReplayLevel.Domain);
        domain.StateStable.ShouldBeTrue("two rebuilds of the stream give the same state");
        domain.ProjectionMatches.ShouldBeTrue(string.Join("; ", domain.ProjectionDifferences));
        domain.Decisions.ShouldNotBeEmpty();
        domain.Decisions.ShouldAllBe(decision => decision.Matches && decision.SettingsRecorded);

        using JsonDocument executions = (await system.Api.GetFromJsonAsync<JsonDocument>($"/api/v1/incidents/{incidentId}/agent-executions"))!;
        Guid[] executionIds = [.. executions.RootElement.EnumerateArray().Select(execution => execution.GetProperty("agentExecutionId").GetGuid())];
        executionIds.ShouldNotBeEmpty();
        bool deterministicModel = SentinelSystem.ModelProvider == "test-double";
        foreach (Guid executionId in executionIds)
        {
            InvestigationReplayResult replay = await PostAsync<InvestigationReplayResult>(
                $"/api/v1/agent-executions/{executionId}/replays", new InvestigationReplayRequest { Level = ReplayLevel.Investigation });
            output.WriteLine($"Investigation replay of {executionId}: {replay.ToolCallsReplayed} tool call(s), deterministic {replay.Deterministic}, findings match {replay.FindingsMatch}");
            replay.Level.ShouldBe(ReplayLevel.Investigation);
            replay.UncapturedQueries.ShouldBe(0, "every provider answer comes from the captured inputs");
            replay.InputsReproduced.ShouldBeTrue(string.Join("; ", replay.ToolCallMismatches));
            replay.UnsupportedCitations.ShouldBeEmpty();
            if (deterministicModel)
            {
                replay.Deterministic.ShouldBeTrue(replay.Explanation);
                replay.FindingsMatch.ShouldBe(true, string.Join("; ", replay.Differences));
            }
        }

        DomainReplayResult after = await PostAsync<DomainReplayResult>($"/api/v1/incidents/{incidentId}/replays/domain", null);
        after.StreamVersion.ShouldBe(domain.StreamVersion, "replays append nothing to the incident");
        after.StateHash.ShouldBe(domain.StateHash);
        (await system.SentinelRollbacksAsync("checkout-service")).ShouldBe(1, "replays execute no action");
    }

    private async Task<T> PostAsync<T>(string path, object? body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = body is null ? null : JsonContent.Create(body, options: SentinelJson.Options)
        };
        request.Headers.Add("Idempotency-Key", $"replay-{Guid.NewGuid():N}");
        using HttpResponseMessage response = await system.Api.SendAsync(request);
        string content = await response.Content.ReadAsStringAsync();
        response.IsSuccessStatusCode.ShouldBeTrue($"POST {path} answered {(int)response.StatusCode}: {content}");
        return JsonSerializer.Deserialize<T>(content, SentinelJson.Options)!;
    }

    private async Task ApproveInBrowserAsync(IncidentDetail incident)
    {
        using IPlaywright playwright = await Playwright.CreateAsync();
        await using IBrowser browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
        IPage page = await NewPageAsync(browser);

        await page.GotoAsync($"{system.WebUrl}/incidents/{incident.Summary.IncidentId}");

        // Phase 08 journeys 1 to 3 before the approval: the impact and its correlated change, the evidence, the competing
        // hypotheses, and the checks the leading explanation survived. The texts come from the incident itself, so the
        // journey holds for a live model's wording as for the test double's.
        HypothesisId? leadingId = incident.Decision!.LeadingHypothesisId;
        string leading = incident.Hypotheses.Single(hypothesis => hypothesis.Finding.HypothesisId == leadingId).Finding.Statement;
        string rival = incident.Hypotheses.First(hypothesis => hypothesis.Finding.HypothesisId != leadingId).Finding.Statement;
        string evidence = incident.Evidence.First(item => item.Status == EvidenceStatus.Accepted).Finding.Statement;
        await Assertions.Expect(page.GetByLabel("Correlated changes")).ToContainTextAsync("checkout-service deploy v2.4.1");
        await Assertions.Expect(page.GetByRole(AriaRole.Table, new PageGetByRoleOptions { Name = "Evidence" })).ToContainTextAsync(evidence);
        ILocator comparison = page.GetByRole(AriaRole.Table, new PageGetByRoleOptions { Name = "Hypothesis comparison" });
        await Assertions.Expect(comparison).ToContainTextAsync(leading);
        await Assertions.Expect(comparison).ToContainTextAsync(rival);
        await Assertions.Expect(page.GetByText("Diagnosed", new PageGetByTextOptions { Exact = true })).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByLabel("Decision checks")).ToContainTextAsync("Passed: ");

        await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Approve plan" }).ClickAsync();
        ILocator dialog = page.GetByRole(AriaRole.Dialog, new PageGetByRoleOptions { Name = $"Approve plan v{incident.Plan!.Plan.PlanVersion}?" });
        await Assertions.Expect(dialog).ToContainTextAsync(incident.Plan.Plan.PlanHash[..20]);
        await dialog.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Confirm approval" }).ClickAsync();

        // The approval reaches Runtime as a message and comes back to the page as a live event, so give it time. The
        // timeline and the policy line also mention the approver; only the plan's confirmation starts with these words.
        await Assertions.Expect(page.GetByText(new Regex("^Approved by responder@example\\.test at "))).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 60_000 });
        output.WriteLine("Approved in Sentinel.Web.");
    }

    private async Task ReviewInBrowserAsync(IncidentDetail resolved)
    {
        using IPlaywright playwright = await Playwright.CreateAsync();
        await using IBrowser browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
        IPage page = await NewPageAsync(browser);

        await page.GotoAsync($"{system.WebUrl}/incidents/{resolved.Summary.IncidentId}");
        await Assertions.Expect(page.GetByRole(AriaRole.Group, new PageGetByRoleOptions { Name = "Execution" })).ToContainTextAsync("Action verified at the target");
        await Assertions.Expect(page.GetByRole(AriaRole.Group, new PageGetByRoleOptions { Name = "Recovery" })).ToContainTextAsync("Recovered");

        await page.GetByRole(AriaRole.Link, new PageGetByRoleOptions { Name = "Postmortem and replay" }).ClickAsync();
        await Assertions.Expect(page.GetByRole(AriaRole.Heading, new PageGetByRoleOptions { Level = 1, Name = $"Postmortem: {resolved.Summary.Title}" })).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByRole(AriaRole.List, new PageGetByRoleOptions { Name = "Decision" })).ToContainTextAsync("Diagnosed by consensus-v4");

        await page.GetByRole(AriaRole.Textbox, new PageGetByRoleOptions { Name = "Narrative" }).FillAsync("checkout-service v2.4.1 leaked Redis connections; rolling back to v2.4.0 restored the SLO.");
        await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Save narrative" }).ClickAsync();
        await Assertions.Expect(page.GetByText("Saved as version 1.")).ToBeVisibleAsync();

        await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Replay from the event stream" }).ClickAsync();
        await Assertions.Expect(page.GetByText(new Regex(@"Replayed \d+ events: the incident reproduced exactly\."))).ToBeVisibleAsync();
        output.WriteLine("Reviewed the recovery, the postmortem and a replay in Sentinel.Web.");
    }

    // Sentinel.Web runs under next dev, which compiles a route on its first visit, so assertions get the actions' budget
    // instead of Playwright's five seconds.
    private static async Task<IPage> NewPageAsync(IBrowser browser)
    {
        IPage page = await browser.NewPageAsync();
        page.SetDefaultTimeout(60_000);
        Assertions.SetDefaultExpectTimeout(60_000);
        return page;
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
