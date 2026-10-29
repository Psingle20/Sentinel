using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Playwright;
using Reference.Scenarios.Api.Contracts;
using Reference.Scenarios.Api.Manifests;
using Reference.Scenarios.Application.Runs;
using Reference.Scenarios.Domain.Scenarios;
using Sentinel.Contracts.Incidents;
using Sentinel.Contracts.Investigations;
using Sentinel.Contracts.Messaging;
using Sentinel.Contracts.Remediation;
using Shouldly;
using Xunit.Abstractions;

namespace Sentinel.EndToEnd.Tests;

/// <summary>
/// The Phase 01A walking skeleton, end to end across real process boundaries: the reference
/// system breaks, Ingestion observes it, Runtime correlates and decides, AgentWorker investigates
/// with the model test double, a responder approves in Sentinel.Web, Executor rolls back through
/// the deploy controller, and Runtime verifies recovery from telemetry.
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
            IncidentDetail incident = await system.WaitForIncidentAsync(detail => detail.Summary.Status == IncidentStatus.AwaitingApproval, DetectionTimeout, "S1 awaits approval");
            Log(incident);

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

    private async Task ApproveInBrowserAsync(IncidentDetail incident)
    {
        using IPlaywright playwright = await Playwright.CreateAsync();
        await using IBrowser browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
        IPage page = await browser.NewPageAsync();
        page.SetDefaultTimeout(60_000);

        await page.GotoAsync($"{system.WebUrl}/incidents/{incident.Summary.IncidentId}");
        await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Approve plan" }).ClickAsync();
        ILocator dialog = page.GetByRole(AriaRole.Dialog, new PageGetByRoleOptions { Name = $"Approve plan v{incident.Plan!.Plan.PlanVersion}?" });
        await Assertions.Expect(dialog).ToContainTextAsync(incident.Plan.Plan.PlanHash[..20]);
        await dialog.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Confirm approval" }).ClickAsync();

        await Assertions.Expect(page.GetByText("Approved by responder@example.test")).ToBeVisibleAsync();
        output.WriteLine("Approved in Sentinel.Web.");
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
