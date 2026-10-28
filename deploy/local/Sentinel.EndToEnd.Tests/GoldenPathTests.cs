using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Playwright;
using Reference.Scenarios;
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

/// <summary>Sentinel-side helpers for the end-to-end suites.</summary>
public static class SentinelSystemExtensions
{
    public static async Task<IReadOnlyList<IncidentSummary>> IncidentsAsync(this SentinelSystem system) =>
        await system.Api.GetFromJsonAsync<List<IncidentSummary>>("/api/v1/incidents", SentinelJson.Options) ?? [];

    public static async Task<IncidentDetail> WaitForIncidentAsync(this SentinelSystem system, Func<IncidentDetail, bool> condition, TimeSpan timeout, string description)
    {
        DateTime deadline = DateTime.UtcNow + timeout;
        while (true)
        {
            foreach (IncidentSummary summary in (await system.IncidentsAsync()).Where(summary => summary.OpenedAt >= system.ScenarioStartedAt))
            {
                IncidentDetail detail = (await system.Api.GetFromJsonAsync<IncidentDetail>($"/api/v1/incidents/{summary.IncidentId}", SentinelJson.Options))!;
                if (condition(detail))
                {
                    return detail;
                }
            }

            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException($"Timed out after {timeout} waiting until {description}.");
            }

            await Task.Delay(TimeSpan.FromSeconds(3));
        }
    }

    /// <summary>
    /// Phase 02 exit criterion: the golden scenario produced queryable canonical observations of every
    /// kind, with provenance to the provider and redaction applied before they were stored.
    /// </summary>
    public static async Task ShouldHaveEverySignalAsync(this SentinelSystem system, ITestOutputHelper output)
    {
        ArgumentNullException.ThrowIfNull(output);

        var range = new Sentinel.Contracts.TimeRange(system.ScenarioStartedAt.AddMinutes(-15), DateTimeOffset.UtcNow);
        foreach (Sentinel.Contracts.Observations.ObservationKind kind in Enum.GetValues<Sentinel.Contracts.Observations.ObservationKind>())
        {
            Sentinel.Contracts.Queries.ObservationQueryResult result = await Eventually.ValueAsync(
                async () =>
                {
                    using HttpResponseMessage response = await system.Ingestion.PostAsJsonAsync("/v1/observations/query", new Sentinel.Contracts.Queries.ObservationQuery
                    {
                        TenantId = Sentinel.Contracts.Primitives.Identifiers.TenantId.From("acme"),
                        EnvironmentId = Sentinel.Contracts.Primitives.Identifiers.EnvironmentId.From("reference"),
                        Kind = kind,
                        Range = range,
                        Limit = 200
                    }, SentinelJson.Options);
                    response.EnsureSuccessStatusCode();
                    return (await response.Content.ReadFromJsonAsync<Sentinel.Contracts.Queries.ObservationQueryResult>(SentinelJson.Options))!;
                },
                current => current.Observations.Count > 0,
                TimeSpan.FromMinutes(2),
                $"Ingestion has {kind} observations");

            output.WriteLine($"  {kind}: {result.Observations.Count} observation(s) from {string.Join(", ", result.Observations.Select(observation => observation.SourceProvider).Distinct())}; completeness {result.Metadata.Completeness}, observed through {result.Metadata.ObservedThrough:O}");
            result.Observations.ShouldAllBe(observation => !string.IsNullOrEmpty(observation.SourceProvider) && !string.IsNullOrEmpty(observation.SourceResource));
            result.Metadata.RedactionPolicyVersion.ShouldNotBeNullOrEmpty();
        }
    }

    /// <summary>Rollbacks Executor started against a service, from the deploy controller's history.</summary>
    public static async Task<int> SentinelRollbacksAsync(this SentinelSystem system, string service)
    {
        using JsonDocument deployments = (await system.DeployController.GetFromJsonAsync<JsonDocument>($"/api/deployments?service={service}&since={Uri.EscapeDataString(system.ScenarioStartedAt.ToString("O"))}"))!;
        return deployments.RootElement.EnumerateArray().Count(deployment =>
            deployment.GetProperty("kind").GetString() == "rollback" && deployment.GetProperty("actor").GetString() == "sentinel-executor");
    }

    /// <summary>
    /// Waits until the reference system is back at baseline with a healthy SLO for a full minute
    /// and no incident is left open, so each scenario starts clean whatever ran before it,
    /// including the start-up of the system itself.
    /// </summary>
    public static async Task WaitUntilQuietAsync(this SentinelSystem system, ITestOutputHelper output)
    {
        (await system.ScenarioRunner.PostAsync("/api/reset", null)).EnsureSuccessStatusCode();
        (await system.ScenarioRunner.PostAsync("/api/actions/stop-batch", null)).EnsureSuccessStatusCode();
        await Eventually.ValueAsync(
            async () => await system.Prometheus.ScalarAsync("max_over_time(slo:checkout_success:burn_rate1m[1m])") ?? 0,
            burn => burn < 1,
            TimeSpan.FromMinutes(6),
            "the checkout SLO has been healthy for a minute",
            TimeSpan.FromSeconds(5));
        await Eventually.ValueAsync(
            async () => (await system.IncidentsAsync()).Count(incident => incident.Status != IncidentStatus.Resolved),
            open => open == 0,
            TimeSpan.FromMinutes(6),
            "every incident is resolved",
            TimeSpan.FromSeconds(5));
        system.ScenarioStartedAt = DateTimeOffset.UtcNow;
        output.WriteLine($"Quiet at {system.ScenarioStartedAt:O}.");
    }

    /// <summary>
    /// Records Sentinel's and the shadow deploy-blame baseline's outcome for a scenario under
    /// artifacts/reports/walking-skeleton, and regenerates the comparison report from every record.
    /// </summary>
    public static async Task RecordOutcomeAsync(this SentinelSystem system, string scenarioId, IncidentDetail incident, bool approved)
    {
        ScenarioManifest manifest = system.Scenarios.Manifests[scenarioId];
        using JsonDocument executions = (await system.AgentWorker.GetFromJsonAsync<JsonDocument>($"/v1/executions?incidentId={incident.Summary.IncidentId}"))!;
        JsonElement? baseline = executions.RootElement.EnumerateArray()
            .Where(execution => execution.GetProperty("shadow").GetBoolean())
            .Select(execution => (JsonElement?)execution)
            .FirstOrDefault();
        InvestigationCompleted? baselineFindings = baseline is { } shadow
            ? JsonSerializer.Deserialize<InvestigationCompleted>(shadow.GetProperty("findings").GetString()!, SentinelJson.Options)
            : null;

        var record = OutcomeRecord.From(manifest, incident, baselineFindings, approved);
        await OutcomeRecord.WriteAsync(record, incident);
    }
}
