using System.Buffers.Text;
using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Sentinel.Contracts.Messaging;
using Sentinel.Contracts.Remediation;
using Sentinel.Executor.Application;
using Sentinel.Executor.Domain.Capabilities;
using Sentinel.Executor.Application.Abstractions.Remediation;

namespace Sentinel.Executor.Infrastructure.Remediation;

/// <summary>
/// Rolls a reference-system service back through the deploy controller (Phase 01A). Executor is
/// the only holder of the deploy token. The request's idempotency key is sent with the rollback,
/// so a repeated call returns the original deployment instead of starting another.
/// </summary>
internal sealed class DeployControllerRollbackAdapter(HttpClient http, TimeProvider time) : IRemediationAdapter
{
    public const string Actor = "sentinel-executor";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public string ActionKind => "rollback";

    /// <summary>A rollback may move one revision, to exactly the plan's target revision.</summary>
    public IReadOnlyDictionary<string, string> CapabilityLimits(RemediationAction action) =>
        new Dictionary<string, string>(StringComparer.Ordinal) { ["from"] = AsRollback(action).FromRevision, ["to"] = AsRollback(action).ToRevision };

    public async Task<TargetInspection> InspectAsync(RemediationAction action, string idempotencyKey, CancellationToken cancellationToken)
    {
        RollbackAction rollback = AsRollback(action);
        ServiceStatus? service = await http.GetFromJsonAsync<ServiceStatus>($"/api/services/{rollback.TargetServiceId}", Json, cancellationToken);
        List<Deployment> deployments = await http.GetFromJsonAsync<List<Deployment>>($"/api/deployments?service={rollback.TargetServiceId}", Json, cancellationToken) ?? [];
        Deployment? ours = deployments.FindLast(deployment => deployment.IdempotencyKey == idempotencyKey);

        OperationStatus status = ours?.Status switch
        {
            null => OperationStatus.NotFound,
            "inProgress" => OperationStatus.InProgress,
            "completed" => OperationStatus.Completed,
            _ => OperationStatus.Failed
        };
        string detail = ours is null
            ? $"{rollback.TargetServiceId} serves {service?.ServingRevision}; no rollback with this request's key exists."
            : string.Create(CultureInfo.InvariantCulture, $"{rollback.TargetServiceId} serves {service?.ServingRevision}; rollback {ours.DeploymentId} to {ours.Version} is {ours.Status}.");
        return new TargetInspection(service?.ServingRevision, status, detail);
    }

    public async Task<AdapterResult> ExecuteAsync(RemediationAction action, Capability capability, string idempotencyKey, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(capability);

        RollbackAction rollback = AsRollback(action);
        if (CapabilityCheck.Refuse(capability, ActionKind, rollback.TargetServiceId.Value, time) is { } refused)
        {
            return refused;
        }

        if (capability.Limits.GetValueOrDefault("to") != rollback.ToRevision)
        {
            return new AdapterResult(AdapterOutcome.Rejected, $"Capability {capability.CapabilityId} does not cover a rollback to {rollback.ToRevision}.");
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/services/{rollback.TargetServiceId}/deployments")
        {
            Content = JsonContent.Create(new
            {
                version = rollback.ToRevision,
                kind = "rollback",
                actor = Actor,
                reason = $"Sentinel remediation, action {rollback.ActionId}"
            })
        };
        request.Headers.Add("Idempotency-Key", idempotencyKey);
        using HttpResponseMessage response = await http.SendAsync(request, cancellationToken);

        if (response.IsSuccessStatusCode)
        {
            Deployment? deployment = await response.Content.ReadFromJsonAsync<Deployment>(Json, cancellationToken);
            return new AdapterResult(AdapterOutcome.Accepted, $"Rollback of {rollback.TargetServiceId} to {rollback.ToRevision} accepted as deployment {deployment?.DeploymentId}.");
        }

        return response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.NotFound or HttpStatusCode.Conflict or HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
            ? new AdapterResult(AdapterOutcome.Rejected, $"The deploy controller refused the rollback with {(int)response.StatusCode}.")
            : new AdapterResult(AdapterOutcome.Unknown, $"The deploy controller answered {(int)response.StatusCode}; the rollback may or may not have started.");
    }

    public async Task<bool> VerifyActionAsync(RemediationAction action, string idempotencyKey, CancellationToken cancellationToken)
    {
        TargetInspection target = await InspectAsync(action, idempotencyKey, cancellationToken);
        return target.Operation == OperationStatus.Completed && target.ServingRevision == AsRollback(action).ToRevision;
    }

    // Compensating a rollback would redeploy the faulty revision; that is a human decision.
    public Task<AdapterResult> CompensateAsync(RemediationAction action, Capability capability, string idempotencyKey, CancellationToken cancellationToken) =>
        Task.FromResult(new AdapterResult(AdapterOutcome.Rejected, "Rollbacks are not compensated automatically: redeploying the faulty revision is a human decision."));

    private static RollbackAction AsRollback(RemediationAction action) =>
        action as RollbackAction ?? throw new ArgumentException($"{action?.GetType().Name} is not a rollback.", nameof(action));

    private sealed record ServiceStatus(string Name, string? ServingRevision);

    private sealed record Deployment(string DeploymentId, string Version, string Status, string? IdempotencyKey);
}
