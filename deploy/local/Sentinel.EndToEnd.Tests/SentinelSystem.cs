using System.Net.Http.Headers;
using System.Net.Http.Json;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Reference.Scenarios;

namespace Sentinel.EndToEnd.Tests;

/// <summary>
/// The whole local system, started once per test collection from the AppHost with empty
/// databases: Sentinel's five services, the web app and the reference system.
/// </summary>
public class SentinelSystem : IAsyncLifetime
{
    public static readonly TimeSpan StartupTimeout = TimeSpan.FromMinutes(6);

    private static readonly string[] ReferenceResources =
    [
        "checkout-service", "order-service", "reporting-worker", "redis-exporter", "deploy-controller", "scenario-runner", "prometheus"
    ];

    private static readonly string[] SentinelResources = ["ingestion-service", "runtime-service", "agent-worker-service", "executor-service", "gateway-service", "web"];

    private readonly bool _referenceOnly;

    public SentinelSystem()
        : this(referenceOnly: false)
    {
    }

    protected SentinelSystem(bool referenceOnly) => _referenceOnly = referenceOnly;

    public DistributedApplication App { get; private set; } = null!;

    public ResourceLogs Logs { get; } = new();

    public IReadOnlyList<string> ResourceNames => _referenceOnly ? ReferenceResources : [.. ReferenceResources, .. SentinelResources];

    public ScenarioCatalog Scenarios { get; } = ScenarioCatalog.Load(Path.Combine(AppContext.BaseDirectory, "manifests"));

    public PrometheusClient Prometheus { get; private set; } = null!;

    public HttpClient ScenarioRunner { get; private set; } = null!;

    public HttpClient DeployController { get; private set; } = null!;

    /// <summary>Sentinel.Api, calling as the default local development identity.</summary>
    public HttpClient Api { get; private set; } = null!;

    public HttpClient AgentWorker { get; private set; } = null!;

    /// <summary>Sentinel.Ingestion's read-only observation query contract.</summary>
    public HttpClient Ingestion { get; private set; } = null!;

    /// <summary>Sentinel.Runtime, for the run records the evaluation harness scores.</summary>
    public HttpClient Runtime { get; private set; } = null!;

    /// <summary>The deploy controller's guarded control endpoints, for injecting the Phase 07 failure scenarios' faults.</summary>
    public HttpClient DeployControllerControl { get; private set; } = null!;

    public Uri WebUrl { get; private set; } = null!;

    /// <summary>When the current scenario started from a quiet system; incidents before it are ignored.</summary>
    public DateTimeOffset ScenarioStartedAt { get; set; } = DateTimeOffset.MinValue;

    public async Task InitializeAsync()
    {
        IDistributedApplicationTestingBuilder builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.Sentinel_AppHost>(
            ["Sentinel:EphemeralStorage=true", $"Sentinel:ReferenceOnly={_referenceOnly}"]);
        // Resource output goes only to the in-memory capture, printed when a test fails.
        builder.Services.AddLogging(logging => logging
            .ClearProviders()
            .AddProvider(Logs)
            .AddFilter("System.Net.Http", LogLevel.Warning)
            .SetMinimumLevel(LogLevel.Information));
        App = await builder.BuildAsync();

        using var startup = new CancellationTokenSource(StartupTimeout);
        await App.StartAsync(startup.Token);
        foreach (string resource in ResourceNames)
        {
            await App.ResourceNotifications.WaitForResourceHealthyAsync(resource, startup.Token);
        }

        Prometheus = new PrometheusClient(App.CreateHttpClient("prometheus", "http"));

        ScenarioRunner = App.CreateHttpClient("scenario-runner");
        ScenarioRunner.DefaultRequestHeaders.Add("X-Reference-Control-Token", await ParameterAsync("reference-control-token"));

        DeployController = App.CreateHttpClient("deploy-controller");
        DeployController.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await ParameterAsync("reference-deploy-token"));

        if (!_referenceOnly)
        {
            Api = App.CreateHttpClient("gateway-service");
            AgentWorker = App.CreateHttpClient("agent-worker-service");
            Ingestion = App.CreateHttpClient("ingestion-service");
            Runtime = App.CreateHttpClient("runtime-service");
            DeployControllerControl = App.CreateHttpClient("deploy-controller");
            DeployControllerControl.DefaultRequestHeaders.Add("X-Reference-Control-Token", await ParameterAsync("reference-control-token"));
            WebUrl = App.GetEndpoint("web", "http");

            // The browser that performs approvals; already present when Sentinel.Web's own Playwright is installed.
            int installed = Microsoft.Playwright.Program.Main(["install", "chromium"]);
            if (installed != 0)
            {
                throw new InvalidOperationException($"Installing Playwright's Chromium failed with exit code {installed}.");
            }
        }
    }

    /// <summary>Prints the run, key reference signals and every resource's recent logs after a failure.</summary>
    public async Task ReportAsync(Xunit.Abstractions.ITestOutputHelper output, string runId)
    {
        ArgumentNullException.ThrowIfNull(output);

        ScenarioRunView? view = await ScenarioRunner.GetFromJsonAsync<ScenarioRunView>($"/api/runs/{runId}");
        output.WriteLine($"Run {runId}: phase {view?.Phase}, error {view?.Error}");
        foreach (StepExecution step in view?.Steps ?? [])
        {
            output.WriteLine($"  +{step.AtSeconds}s {step.Action} {step.Service} {step.Version}: executed {step.ExecutedAt:O} error {step.Error}");
        }

        foreach (string query in DiagnosticQueries)
        {
            output.WriteLine($"  {query} = {await Prometheus.ScalarAsync(query)}");
        }

        output.WriteLine($"  Redis clients by name: {await Prometheus.VectorAsync("sum by (client_name) (reference_redis_clients)", "client_name")}");
        if (!_referenceOnly)
        {
            await ReportIncidentsAsync(output);
        }

        output.WriteLine(Logs.Dump(ResourceNames, 25));
    }

    /// <summary>What Sentinel concluded about each incident of the scenario: how far it got, and why it stopped there.</summary>
    private async Task ReportIncidentsAsync(Xunit.Abstractions.ITestOutputHelper output)
    {
        List<Sentinel.Contracts.Incidents.IncidentSummary> incidents = await Api.GetFromJsonAsync<List<Sentinel.Contracts.Incidents.IncidentSummary>>("/api/v1/incidents", Sentinel.Contracts.Messaging.SentinelJson.Options) ?? [];
        foreach (Sentinel.Contracts.Incidents.IncidentSummary summary in incidents.Where(incident => incident.OpenedAt >= ScenarioStartedAt))
        {
            Sentinel.Contracts.Incidents.IncidentDetail detail = (await Api.GetFromJsonAsync<Sentinel.Contracts.Incidents.IncidentDetail>($"/api/v1/incidents/{summary.IncidentId}", Sentinel.Contracts.Messaging.SentinelJson.Options))!;
            output.WriteLine($"Incident {summary.IncidentId} {summary.Title}: {summary.Status}, cycle {detail.Cycle}");
            foreach (Sentinel.Contracts.Incidents.InvestigationView investigation in detail.Investigations)
            {
                output.WriteLine($"  investigation {investigation.Role ?? "generalist"}: {investigation.Outcome}{(investigation.TimedOut ? " (timed out)" : string.Empty)} {investigation.AbstentionReason}");
            }

            foreach (Sentinel.Contracts.Incidents.HypothesisView hypothesis in detail.Hypotheses)
            {
                output.WriteLine($"  hypothesis {hypothesis.Finding.HypothesisId} {hypothesis.Finding.Key} ({hypothesis.Status}): {hypothesis.Finding.Statement}");
            }

            if (detail.Decision is { } decision)
            {
                output.WriteLine($"  decision {decision.Outcome} ({decision.AlgorithmVersion}) confidence {decision.Confidence:F2}, completeness {decision.EvidenceCompleteness:F2}, leading {decision.LeadingHypothesisId}: {decision.Rationale}");
                foreach (Sentinel.Contracts.Incidents.HypothesisAssessmentView assessment in decision.Assessments)
                {
                    output.WriteLine($"    {assessment.HypothesisId} score {assessment.Score:F2}, support {assessment.IndependentSupport}, contradiction {assessment.IndependentContradiction}, eliminated {assessment.Eliminated}: {assessment.Reason}");
                }

                foreach (string missing in decision.MissingEvidence)
                {
                    output.WriteLine($"    missing: {missing}");
                }
            }

            if (detail.Plan is { } plan)
            {
                output.WriteLine($"  plan v{plan.Plan.PlanVersion} {plan.Status}: policy {plan.PolicyEvaluation.PolicyVersion} {plan.PolicyEvaluation.Decision} [{string.Join("; ", plan.PolicyEvaluation.RuleTrace)}]");
            }

            foreach (Sentinel.Contracts.Incidents.TimelineEntry entry in detail.Timeline)
            {
                output.WriteLine($"  v{entry.StreamVersion} {entry.OccurredAt:HH:mm:ss} {entry.Summary}");
            }
        }
    }

    private static readonly string[] DiagnosticQueries =
    [
        "slo:checkout_success:burn_rate1m",
        "sum(reference_redis_connected_clients)",
        "histogram_quantile(0.95, sum by (le) (rate(reference_orders_duration_seconds_bucket[1m])))",
        "histogram_quantile(0.95, sum by (le) (rate(reference_checkout_duration_seconds_bucket[1m])))",
        "sum by (outcome) (rate(reference_checkout_requests_total[1m]))"
    ];

    public async Task DisposeAsync()
    {
        ScenarioRunner?.Dispose();
        Api?.Dispose();
        AgentWorker?.Dispose();
        Ingestion?.Dispose();
        Runtime?.Dispose();
        DeployControllerControl?.Dispose();
        DeployController?.Dispose();
        Prometheus?.Dispose();
        if (App is not null)
        {
            await App.DisposeAsync();
        }
    }

    /// <summary>Stops or starts a resource through Aspire's resource commands, as an operator would from the dashboard.</summary>
    public async Task CommandAsync(string resource, string command)
    {
        ExecuteCommandResult result = await App.ResourceCommands.ExecuteCommandAsync(resource, command, CancellationToken.None);
        if (!result.Success)
        {
            throw new InvalidOperationException($"{command} on {resource} failed: {result.Message}");
        }
    }

    public async Task<string> ParameterAsync(string name)
    {
        ParameterResource parameter = App.Services.GetRequiredService<DistributedApplicationModel>()
            .Resources.OfType<ParameterResource>()
            .Single(resource => resource.Name == name);
        return await parameter.GetValueAsync(CancellationToken.None) ?? throw new InvalidOperationException($"{name} has no value.");
    }

    /// <summary>Starts a scenario run and waits until its first step executes.</summary>
    public async Task<ScenarioRunView> StartScenarioAsync(string scenarioId)
    {
        using HttpResponseMessage response = await ScenarioRunner.PostAsJsonAsync("/api/runs", new StartRunRequest(scenarioId));
        response.EnsureSuccessStatusCode();
        ScenarioRunView run = (await response.Content.ReadFromJsonAsync<ScenarioRunView>())!;

        ScenarioManifest manifest = Scenarios.Manifests[scenarioId];
        var timeout = TimeSpan.FromSeconds(manifest.WarmupSeconds + 60);
        return await Eventually.ValueAsync(
            async () => await ScenarioRunner.GetFromJsonAsync<ScenarioRunView>($"/api/runs/{run.RunId}"),
            current => current?.Phase is "running" or "observing" or "completed" or "failed",
            timeout,
            $"run {run.RunId} activates") ?? throw new InvalidOperationException("The run disappeared.");
    }

    /// <summary>Stops a run if it is still going; safe to call for a finished run.</summary>
    public async Task StopScenarioAsync(string runId)
    {
        using HttpResponseMessage response = await ScenarioRunner.PostAsync($"/api/runs/{runId}/stop", null);
        response.EnsureSuccessStatusCode();
        await WaitForRunToFinishAsync(runId, TimeSpan.FromMinutes(1));
    }

    public async Task<ScenarioRunView> WaitForRunToFinishAsync(string runId, TimeSpan timeout) =>
        await Eventually.ValueAsync(
            async () => await ScenarioRunner.GetFromJsonAsync<ScenarioRunView>($"/api/runs/{runId}"),
            current => current?.CompletedAt is not null,
            timeout,
            $"run {runId} completes") ?? throw new InvalidOperationException("The run disappeared.");
}

/// <summary>The reference system alone, for the Phase 01 scenario suite.</summary>
public sealed class ReferenceOnlySystem : SentinelSystem
{
    public ReferenceOnlySystem()
        : base(referenceOnly: true)
    {
    }
}

[CollectionDefinition(Name)]
public sealed class SystemCollection : ICollectionFixture<SentinelSystem>
{
    public const string Name = "system";
}

[CollectionDefinition(Name)]
public sealed class ReferenceCollection : ICollectionFixture<ReferenceOnlySystem>
{
    public const string Name = "reference";
}

public static class Eventually
{
    public static async Task<T> ValueAsync<T>(Func<Task<T>> read, Func<T, bool> condition, TimeSpan timeout, string description, TimeSpan? interval = null)
    {
        DateTime deadline = DateTime.UtcNow + timeout;
        T value = await read();
        while (!condition(value))
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException($"Timed out after {timeout} waiting until {description}.");
            }

            await Task.Delay(interval ?? TimeSpan.FromSeconds(2));
            value = await read();
        }

        return value;
    }
}
