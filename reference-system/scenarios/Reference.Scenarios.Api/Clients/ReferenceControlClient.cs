using System.Net.Http.Headers;
using Reference.Common.Control;
using Reference.Scenarios.Application.Abstractions;
using Reference.Scenarios.Domain.Scenarios;

namespace Reference.Scenarios.Api.Clients;

/// <summary>
/// The runner's view of the reference system: the deploy controller, as a CI pipeline would use
/// it, the reporting-worker's batch control and the queue-consumer's queue. Tokens come from
/// configuration, never source.
/// </summary>
public sealed class ReferenceControlClient(HttpClient http, IConfiguration configuration) : IReferenceControl
{
    public const string Actor = "reference-pipeline";

    public async Task ResetAsync(CancellationToken cancellationToken)
    {
        await SendControlAsync(HttpMethod.Post, Address("deploy-controller", "/_reference/reset"), cancellationToken);
        await StopBatchAsync(cancellationToken);
        // In the AWS environment the queue-consumer runs as an ECS task, and the environment resets its queue itself.
        if (configuration["services:queue-consumer:http:0"] is not null)
        {
            await SendControlAsync(HttpMethod.Post, Address("queue-consumer", "/_reference/queue/reset"), cancellationToken);
        }

        await SendControlAsync(HttpMethod.Post, Address("order-service", "/_reference/orders/reset"), cancellationToken);
    }

    public async Task DeployAsync(ScenarioStep step, string runId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(step);

        object body = step.Action == "configChange"
            ? new { version = "current", kind = "configChange", actor = Actor, reason = $"scenario run {runId}", configurationDiff = step.ConfigurationDiff }
            : new { version = step.Version, kind = "deploy", actor = Actor, reason = $"scenario run {runId}", rolloutSeconds = step.RolloutSeconds };

        using var request = new HttpRequestMessage(HttpMethod.Post, Address("deploy-controller", $"/api/services/{step.Service}/deployments"))
        {
            Content = JsonContent.Create(body)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Require("DeployController:DeployToken"));
        request.Headers.Add("Idempotency-Key", $"{runId}:{step.AtSeconds}:{step.Service}");
        await SendAsync(request, cancellationToken);
    }

    public Task StartBatchAsync(ScenarioStep step, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(step);

        return SendControlAsync(
            HttpMethod.Post,
            Address(step.Service, "/_reference/batch/start"),
            cancellationToken,
            new { targetConnections = step.TargetConnections ?? 190, connectionsPerSecond = step.ConnectionsPerSecond ?? 3 });
    }

    public Task StopBatchAsync(CancellationToken cancellationToken) =>
        SendControlAsync(HttpMethod.Post, Address("reporting-worker", "/_reference/batch/stop"), cancellationToken);

    public Task InjectPoisonMessageAsync(ScenarioStep step, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(step);

        return SendControlAsync(HttpMethod.Post, Address(step.Service, "/_reference/queue/poison"), cancellationToken);
    }

    public Task DuplicatePublicationAsync(ScenarioStep step, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(step);

        return SendControlAsync(HttpMethod.Post, Address(step.Service, "/_reference/queue/duplicate-publication"), cancellationToken, new { seconds = step.DurationSeconds ?? 120 });
    }

    public Task SlowConfirmationsAsync(ScenarioStep step, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(step);

        return SendControlAsync(HttpMethod.Post, Address(step.Service, "/_reference/orders/slow-confirmations"), cancellationToken, new { seconds = step.DurationSeconds ?? 180 });
    }

    private Task SendControlAsync(HttpMethod method, Uri uri, CancellationToken cancellationToken, object? body = null)
    {
        var request = new HttpRequestMessage(method, uri);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        request.Headers.Add(FailureControl.TokenHeader, Require("Reference:ControlToken"));
        return SendAndDisposeAsync(request, cancellationToken);
    }

    private async Task SendAndDisposeAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        using (request)
        {
            await SendAsync(request, cancellationToken);
        }
    }

    private async Task SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            string detail = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new HttpRequestException($"{request.Method} {request.RequestUri} returned {(int)response.StatusCode}: {detail}");
        }
    }

    private Uri Address(string service, string path) =>
        new(new Uri(configuration[$"services:{service}:http:0"] ?? throw new InvalidOperationException($"The address of {service} is not configured.")), path);

    private string Require(string key) =>
        configuration[key] ?? throw new InvalidOperationException($"{key} is not configured.");
}
