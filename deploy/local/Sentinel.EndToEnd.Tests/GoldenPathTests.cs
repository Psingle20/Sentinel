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
        IPage page = await browser.NewPageAsync();
        page.SetDefaultTimeout(60_000);

        await page.GotoAsync($"{system.WebUrl}/incidents/{incident.Summary.IncidentId}");
        await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Approve plan" }).ClickAsync();
        ILocator dialog = page.GetByRole(AriaRole.Dialog, new PageGetByRoleOptions { Name = $"Approve plan v{incident.Plan!.Plan.PlanVersion}?" });
        await Assertions.Expect(dialog).ToContainTextAsync(incident.Plan.Plan.PlanHash[..20]);
        await dialog.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Confirm approval" }).ClickAsync();

        // The approval reaches Runtime as a message and comes back to the page as a live event, so give it time.
        await Assertions.Expect(page.GetByText("Approved by responder@example.test")).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 60_000 });
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
