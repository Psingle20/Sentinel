using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Reference.Common.Control;
using Shouldly;
using Reference.DeployController.Api.Clients;
using Reference.DeployController.Application.Abstractions;
using DeployControllerEntryPoint = Reference.DeployController.Api.ApiDependencyModule;

namespace Reference.Tests;

/// <summary>
/// The deploy controller's HTTP surface as Sentinel and the scenario runner use it: token checks,
/// service views, deployments with their idempotency and conflicts, and the guarded reset and fault
/// controls. The reference services are a fake that answers and switches revisions.
/// </summary>
public sealed class DeployControllerEndpointTests : IDisposable
{
    private const string DeployToken = "deploy-test-not-a-token";
    private const string ReadToken = "read-test-not-a-token";
    private const string ControlToken = "control-test-not-a-token";

    private readonly DeployControllerHost _host = new();
    private readonly HttpClient _client;

    public DeployControllerEndpointTests() => _client = _host.CreateClient();

    public void Dispose()
    {
        _client.Dispose();
        _host.Dispose();
    }

    [Fact]
    public async Task Reads_need_either_token_and_describe_every_managed_service()
    {
        using HttpResponseMessage anonymous = await SendAsync(HttpMethod.Get, "/api/services", token: null);
        using HttpResponseMessage services = await SendAsync(HttpMethod.Get, "/api/services", ReadToken);
        using HttpResponseMessage one = await SendAsync(HttpMethod.Get, "/api/services/checkout-service", DeployToken);
        using HttpResponseMessage unknown = await SendAsync(HttpMethod.Get, "/api/services/billing-service", ReadToken);

        anonymous.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        services.StatusCode.ShouldBe(HttpStatusCode.OK);
        JsonElement[] views = [.. (await BodyAsync(services)).EnumerateArray()];
        views.Select(view => view.GetProperty("name").GetString()).ShouldBe(["checkout-service", "order-service", "reporting-worker"]);
        JsonElement checkout = await BodyAsync(one);
        Names(checkout).ShouldBe(["name", "servingRevision", "baselineRevision", "revisions", "activeDeployment", "error"]);
        checkout.GetProperty("servingRevision").GetString().ShouldBe("v2.4.0");
        checkout.GetProperty("activeDeployment").ValueKind.ShouldBe(JsonValueKind.Null);
        unknown.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_deployment_is_accepted_once_per_key_and_conflicts_while_another_rolls_out()
    {
        using HttpResponseMessage readOnly = await DeployAsync("checkout-service", "v2.4.1", "key-0", ReadToken);
        using HttpResponseMessage accepted = await DeployAsync("checkout-service", "v2.4.1", "key-1");
        using HttpResponseMessage replayed = await DeployAsync("checkout-service", "v2.4.1", "key-1");
        using HttpResponseMessage conflict = await DeployAsync("checkout-service", "v2.4.2", "key-2");
        using HttpResponseMessage unknownService = await DeployAsync("billing-service", "v1.0.0", "key-3");
        using HttpResponseMessage unknownRevision = await DeployAsync("order-service", "v9.9.9", "key-4");

        readOnly.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        accepted.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        JsonElement deployment = await BodyAsync(accepted);
        string id = deployment.GetProperty("deploymentId").GetString()!;
        accepted.Headers.Location!.OriginalString.ShouldBe($"/api/deployments/{id}");
        Names(deployment).ShouldBe([
            "deploymentId", "service", "version", "previousVersion", "kind", "actor", "reason", "configurationDiff", "startedAt", "completedAt", "status", "idempotencyKey", "error"]);
        deployment.GetProperty("kind").GetString().ShouldBe("deploy");
        deployment.GetProperty("status").GetString().ShouldBe("inProgress");
        deployment.GetProperty("previousVersion").GetString().ShouldBe("v2.4.0");
        deployment.GetProperty("actor").GetString().ShouldBe("scenario-runner");
        replayed.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await BodyAsync(replayed)).GetProperty("deploymentId").GetString().ShouldBe(id);
        conflict.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await BodyAsync(conflict)).GetProperty("deploymentId").GetString().ShouldBe(id);
        unknownService.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        unknownRevision.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await BodyAsync(unknownRevision)).GetString().ShouldBe("v9.9.9 is not a revision of order-service.");
    }

    [Fact]
    public async Task Deployments_are_listed_by_service_and_read_by_id()
    {
        using HttpResponseMessage accepted = await DeployAsync("order-service", "v1.8.0", "key-orders");
        string id = (await BodyAsync(accepted)).GetProperty("deploymentId").GetString()!;
        using HttpResponseMessage change = await SendAsync(HttpMethod.Post, "/api/services/reporting-worker/deployments", DeployToken, new { version = "ignored", kind = "configChange", configurationDiff = "batch size 500" }, "key-config");

        using HttpResponseMessage listed = await SendAsync(HttpMethod.Get, "/api/deployments?service=order-service", ReadToken);
        using HttpResponseMessage read = await SendAsync(HttpMethod.Get, $"/api/deployments/{id}", ReadToken);
        using HttpResponseMessage unknown = await SendAsync(HttpMethod.Get, $"/api/deployments/{Guid.NewGuid()}", ReadToken);

        (await BodyAsync(listed)).EnumerateArray().Select(entry => entry.GetProperty("deploymentId").GetString()).ShouldBe([id]);
        (await BodyAsync(read)).GetProperty("service").GetString().ShouldBe("order-service");
        unknown.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        change.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        JsonElement configured = await BodyAsync(change);
        configured.GetProperty("status").GetString().ShouldBe("completed");
        configured.GetProperty("version").GetString().ShouldBe("v3.1.0");
    }

    [Fact]
    public async Task Reset_and_faults_need_the_control_token_and_reset_returns_changed_services_to_baseline()
    {
        _host.Services.GetRequiredService<FakeRevisions>().Serving["order-service"] = "v1.8.0";

        using HttpResponseMessage unauthorized = await ControlAsync("/_reference/reset", null, token: null);
        using HttpResponseMessage faults = await ControlAsync("/_reference/faults", new { failRollbacks = true });
        using HttpResponseMessage reset = await ControlAsync("/_reference/reset", null);
        using HttpResponseMessage health = await _client.GetAsync(new Uri("/health", UriKind.Relative));

        unauthorized.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        faults.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        reset.StatusCode.ShouldBe(HttpStatusCode.OK);
        JsonElement resets = await BodyAsync(reset);
        JsonElement orders = resets.EnumerateArray().Single();
        orders.GetProperty("service").GetString().ShouldBe("order-service");
        orders.GetProperty("kind").GetString().ShouldBe("reset");
        orders.GetProperty("version").GetString().ShouldBe("v1.7.0");
        _host.Services.GetRequiredService<FakeRevisions>().Serving["order-service"].ShouldBe("v1.7.0");
        health.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    private Task<HttpResponseMessage> DeployAsync(string service, string version, string key, string token = DeployToken) =>
        SendAsync(HttpMethod.Post, $"/api/services/{service}/deployments", token, new { version, actor = "scenario-runner", reason = "S1 scenario" }, key);

    private async Task<HttpResponseMessage> ControlAsync(string path, object? body, string? token = ControlToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(path, UriKind.Relative));
        if (token is not null)
        {
            request.Headers.Add(FailureControl.TokenHeader, token);
        }

        if (body is not null)
        {
            request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        }

        return await _client.SendAsync(request);
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, string? token, object? body = null, string? idempotencyKey = null)
    {
        using var request = new HttpRequestMessage(method, new Uri(path, UriKind.Relative));
        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        if (idempotencyKey is not null)
        {
            request.Headers.Add("Idempotency-Key", idempotencyKey);
        }

        if (body is not null)
        {
            request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        }

        return await _client.SendAsync(request);
    }

    private static async Task<JsonElement> BodyAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.Clone();
    }

    private static string[] Names(JsonElement element) => [.. element.EnumerateObject().Select(property => property.Name)];

    /// <summary>The reference services' revision endpoints, keyed by host: each serves its baseline until switched.</summary>
    private sealed class FakeRevisions : HttpMessageHandler
    {
        public ConcurrentDictionary<string, string> Serving { get; } = new(StringComparer.Ordinal)
        {
            ["checkout-service"] = "v2.4.0",
            ["order-service"] = "v1.7.0",
            ["reporting-worker"] = "v3.1.0"
        };

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string service = request.RequestUri!.Host;
            if (request.Method == HttpMethod.Put)
            {
                using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
                Serving[service] = body.RootElement.GetProperty("revision").GetString()!;
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(new { revision = Serving[service] }), Encoding.UTF8, "application/json")
            };
        }
    }

    // A rollout that never finishes during a test keeps the deployment in progress.
    private sealed class DeployControllerHost : WebApplicationFactory<DeployControllerEntryPoint>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment(Environments.Development);
            builder.UseSetting("DeployController:DeployToken", DeployToken);
            builder.UseSetting("DeployController:ReadToken", ReadToken);
            builder.UseSetting("DeployController:RolloutSeconds", "3600");
            builder.UseSetting("Reference:FailureControls:Enabled", "true");
            builder.UseSetting("Reference:ControlToken", ControlToken);
            foreach (string service in new[] { "checkout-service", "order-service", "reporting-worker" })
            {
                builder.UseSetting($"services:{service}:http:0", $"http://{service}");
            }

            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton<FakeRevisions>();
                services.AddHttpClient<IRevisionSwitch, RevisionClient>().ConfigurePrimaryHttpMessageHandler(provider => provider.GetRequiredService<FakeRevisions>());
            });
        }
    }
}
