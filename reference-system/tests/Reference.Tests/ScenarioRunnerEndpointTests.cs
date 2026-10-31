using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Reference.Common.Control;
using Shouldly;
using Reference.Scenarios.Api.Clients;
using Reference.Scenarios.Application.Abstractions;
using ScenarioRunnerEntryPoint = Reference.Scenarios.Api.ApiDependencyModule;

namespace Reference.Tests;

/// <summary>
/// The scenario runner's HTTP surface as the end-to-end suites and the dashboard commands use it: the
/// catalog, runs, traffic and the guarded controls. The reference system is a fake that accepts every
/// control call; traffic generation does not run.
/// </summary>
public sealed class ScenarioRunnerEndpointTests
{
    private const string ControlToken = "control-test-not-a-token";

    [Fact]
    public async Task The_catalog_lists_every_scenario_and_reads_one()
    {
        await using var host = new ScenarioRunnerHost(Environments.Development);
        using HttpClient client = host.CreateClient();

        using HttpResponseMessage list = await client.GetAsync(new Uri("/api/scenarios", UriKind.Relative));
        using HttpResponseMessage one = await client.GetAsync(new Uri("/api/scenarios/S1", UriKind.Relative));
        using HttpResponseMessage unknown = await client.GetAsync(new Uri("/api/scenarios/S99", UriKind.Relative));

        JsonElement[] scenarios = [.. (await BodyAsync(list)).EnumerateArray()];
        scenarios.Select(scenario => scenario.GetProperty("id").GetString()).Order(StringComparer.Ordinal).ShouldBe(["AWS-S0", "AWS-S1", "AWS-S7", "S0", "S1", "S10", "S11", "S2", "S3", "S6", "S7", "S8", "S9"]);
        Names(scenarios[0]).ShouldBe(["id", "slug", "kind", "version", "description"]);
        (await BodyAsync(one)).GetProperty("steps").GetArrayLength().ShouldBeGreaterThan(0);
        unknown.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_runner_refuses_a_scenario_of_another_environment()
    {
        await using var host = new ScenarioRunnerHost(Environments.Development);
        using HttpClient client = host.CreateClient();

        using HttpResponseMessage refused = await CallAsync(client, HttpMethod.Post, "/api/runs", new { scenarioId = "AWS-S0" });
        using HttpResponseMessage environment = await client.GetAsync(new Uri("/api/environment", UriKind.Relative));

        refused.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await BodyAsync(refused)).GetProperty("title").GetString().ShouldBe("Runs.WrongEnvironment");
        JsonElement state = await BodyAsync(environment);
        state.GetProperty("environment").GetString().ShouldBe("local");
        state.GetProperty("ready").GetBoolean().ShouldBeTrue();
        host.Reference.Calls.ShouldBeEmpty("a refused run resets nothing");
    }

    [Fact]
    public async Task A_run_may_warm_up_for_less_than_its_manifest()
    {
        await using var host = new ScenarioRunnerHost(Environments.Development);
        using HttpClient client = host.CreateClient();

        using HttpResponseMessage started = await CallAsync(client, HttpMethod.Post, "/api/runs", new { scenarioId = "S0", warmupSeconds = 0 });
        string runId = (await BodyAsync(started)).GetProperty("runId").GetString()!;
        string phase = "resetting";
        for (int attempt = 0; attempt < 50 && phase is "resetting" or "warmup"; attempt++)
        {
            await Task.Delay(100);
            using HttpResponseMessage read = await client.GetAsync(new Uri($"/api/runs/{runId}", UriKind.Relative));
            phase = (await BodyAsync(read)).GetProperty("phase").GetString()!;
        }

        phase.ShouldBe("running", "S0's 75 s warm-up was waived");
        using HttpResponseMessage stopped = await CallAsync(client, HttpMethod.Post, $"/api/runs/{runId}/stop", null);
    }

    [Fact]
    public async Task A_run_starts_once_at_a_time_and_stops_between_steps()
    {
        await using var host = new ScenarioRunnerHost(Environments.Development);
        using HttpClient client = host.CreateClient();

        using HttpResponseMessage idle = await client.GetAsync(new Uri("/api/runs/current", UriKind.Relative));
        using HttpResponseMessage anonymous = await CallAsync(client, HttpMethod.Post, "/api/runs", new { scenarioId = "S0" }, token: null);
        using HttpResponseMessage unknown = await CallAsync(client, HttpMethod.Post, "/api/runs", new { scenarioId = "S99" });
        using HttpResponseMessage started = await CallAsync(client, HttpMethod.Post, "/api/runs", new { scenarioId = "S0" });
        using HttpResponseMessage busy = await CallAsync(client, HttpMethod.Post, "/api/runs", new { scenarioId = "S1" });
        JsonElement run = await BodyAsync(started);
        string runId = run.GetProperty("runId").GetString()!;
        using HttpResponseMessage current = await client.GetAsync(new Uri("/api/runs/current", UriKind.Relative));
        using HttpResponseMessage read = await client.GetAsync(new Uri($"/api/runs/{runId}", UriKind.Relative));
        using HttpResponseMessage stopped = await CallAsync(client, HttpMethod.Post, $"/api/runs/{runId}/stop", null);
        using HttpResponseMessage stopUnknown = await CallAsync(client, HttpMethod.Post, "/api/runs/S0-19700101T000000/stop", null);
        using HttpResponseMessage missing = await client.GetAsync(new Uri("/api/runs/S0-19700101T000000", UriKind.Relative));

        idle.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        anonymous.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        unknown.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        started.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        started.Headers.Location!.OriginalString.ShouldBe($"/api/runs/{runId}");
        Names(run).ShouldBe(["runId", "scenarioId", "phase", "requestedAt", "activatedAt", "completedAt", "steps", "error"]);
        run.GetProperty("phase").GetString().ShouldBe("resetting");
        busy.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await BodyAsync(busy)).GetProperty("runId").GetString().ShouldBe(runId);
        (await BodyAsync(current)).GetProperty("runId").GetString().ShouldBe(runId);
        (await BodyAsync(read)).GetProperty("scenarioId").GetString().ShouldBe("S0");
        stopped.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        stopUnknown.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        missing.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Reset_stop_batch_and_traffic_go_through_the_guarded_controls()
    {
        await using var host = new ScenarioRunnerHost(Environments.Development);
        using HttpClient client = host.CreateClient();

        using HttpResponseMessage traffic = await client.GetAsync(new Uri("/api/traffic", UriKind.Relative));
        using HttpResponseMessage reset = await CallAsync(client, HttpMethod.Post, "/api/reset", null);
        using HttpResponseMessage stopBatch = await CallAsync(client, HttpMethod.Post, "/api/actions/stop-batch", null);
        using HttpResponseMessage changed = await CallAsync(client, HttpMethod.Put, "/api/traffic", new { requestsPerSecond = 25.0, paused = true });

        traffic.StatusCode.ShouldBe(HttpStatusCode.OK);
        Names(await BodyAsync(traffic)).ShouldBe(["requestsPerSecond", "paused"]);
        reset.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        stopBatch.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        host.Reference.Calls.ShouldContain("POST /_reference/reset");
        host.Reference.Calls.ShouldContain("POST /_reference/batch/stop");
        JsonElement settings = await BodyAsync(changed);
        settings.GetProperty("requestsPerSecond").GetDouble().ShouldBe(25.0);
        settings.GetProperty("paused").GetBoolean().ShouldBeTrue();
    }

    [Fact]
    public async Task The_controls_are_absent_where_failure_controls_are_disabled()
    {
        await using var host = new ScenarioRunnerHost(Environments.Production);
        using HttpClient client = host.CreateClient();

        using HttpResponseMessage start = await CallAsync(client, HttpMethod.Post, "/api/runs", new { scenarioId = "S0" });
        using HttpResponseMessage reset = await CallAsync(client, HttpMethod.Post, "/api/reset", null);
        using HttpResponseMessage scenarios = await client.GetAsync(new Uri("/api/scenarios", UriKind.Relative));

        start.StatusCode.ShouldBeOneOf(HttpStatusCode.NotFound, HttpStatusCode.MethodNotAllowed);
        reset.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        scenarios.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    private static async Task<HttpResponseMessage> CallAsync(HttpClient client, HttpMethod method, string path, object? body, string? token = ControlToken)
    {
        using var request = new HttpRequestMessage(method, new Uri(path, UriKind.Relative));
        if (token is not null)
        {
            request.Headers.Add(FailureControl.TokenHeader, token);
        }

        if (body is not null)
        {
            request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        }

        return await client.SendAsync(request);
    }

    private static async Task<JsonElement> BodyAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.Clone();
    }

    private static string[] Names(JsonElement element) => [.. element.EnumerateObject().Select(property => property.Name)];

    /// <summary>Every reference endpoint the runner calls, answering 200 and recording the call.</summary>
    private sealed class FakeReference : HttpMessageHandler
    {
        public ConcurrentQueue<string> Calls { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls.Enqueue($"{request.Method} {request.RequestUri!.AbsolutePath}");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}", Encoding.UTF8, "application/json") });
        }
    }

    private sealed class ScenarioRunnerHost(string environment) : WebApplicationFactory<ScenarioRunnerEntryPoint>
    {
        public FakeReference Reference { get; } = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment(environment);
            builder.UseSetting("Reference:FailureControls:Enabled", environment == Environments.Development ? "true" : "false");
            builder.UseSetting("Reference:ControlToken", ControlToken);
            builder.UseSetting("DeployController:DeployToken", "deploy-test-not-a-token");
            builder.UseSetting("Reference:ManifestDirectory", Path.Combine(AppContext.BaseDirectory, "manifests"));
            foreach (string service in new[] { "deploy-controller", "checkout-service", "order-service", "reporting-worker", "queue-consumer" })
            {
                builder.UseSetting($"services:{service}:http:0", $"http://{service}");
            }

            builder.ConfigureTestServices(services =>
            {
                foreach (ServiceDescriptor hosted in services.Where(descriptor => descriptor.ServiceType == typeof(IHostedService) && descriptor.ImplementationType?.Name != "GenericWebHostService").ToList())
                {
                    services.Remove(hosted);
                }

                services.AddHttpClient<IReferenceControl, ReferenceControlClient>().ConfigurePrimaryHttpMessageHandler(() => Reference);
            });
        }
    }
}
