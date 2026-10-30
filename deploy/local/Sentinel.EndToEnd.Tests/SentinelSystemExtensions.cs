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
        // The rule is evaluated every 5 s, so a full minute holds 12 samples. Right after start-up the series is missing or
        // younger than a minute, which says nothing about health: the first checkouts can still fail.
        await Eventually.ValueAsync(
            async () => await system.Prometheus.ScalarAsync(
                "max_over_time(slo:checkout_success:burn_rate1m[1m]) and on() count_over_time(slo:checkout_success:burn_rate1m[1m]) >= 11") ?? double.PositiveInfinity,
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
    /// artifacts/reports/walking-skeleton, and regenerates the comparison report from every record. Only the test double's
    /// runs are recorded there: a live model's run is kept in its own evaluation run file, which the harness scores as its
    /// own investigator, so it never replaces the reproducible records the walking skeleton cites.
    /// </summary>
    public static async Task RecordOutcomeAsync(this SentinelSystem system, string scenarioId, IncidentDetail incident, bool approved)
    {
        if (SentinelSystem.ModelProvider != SentinelSystem.TestDouble)
        {
            return;
        }

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
