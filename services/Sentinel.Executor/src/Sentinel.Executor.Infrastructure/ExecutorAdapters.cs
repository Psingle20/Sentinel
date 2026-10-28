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

namespace Sentinel.Executor.Infrastructure;

/// <summary>
/// Resolves approval keys from Api's published key set, caching them. An unknown key ID triggers a
/// refresh at most every 10 seconds, which picks up rotation without letting forged IDs flood Api.
/// </summary>
internal sealed class ApiApprovalKeys(IHttpClientFactory clients, TimeProvider time, ILogger<ApiApprovalKeys> logger) : IApprovalKeyResolver, IDisposable
{
    public const string ClientName = "approval-keys";

    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(10);

    private readonly ConcurrentDictionary<string, ECDsa> _keys = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _refresh = new(1, 1);
    private DateTimeOffset _lastRefresh = DateTimeOffset.MinValue;

    public async Task<ECDsa?> ResolveAsync(string keyId, CancellationToken cancellationToken)
    {
        if (_keys.TryGetValue(keyId, out ECDsa? key))
        {
            return key;
        }

        await _refresh.WaitAsync(cancellationToken);
        try
        {
            if (!_keys.ContainsKey(keyId) && time.GetUtcNow() - _lastRefresh >= RefreshInterval)
            {
                _lastRefresh = time.GetUtcNow();
                using HttpClient http = clients.CreateClient(ClientName);
                ApprovalKeySet? set = await http.GetFromJsonAsync<ApprovalKeySet>(ApprovalAttestation.KeysPath, SentinelJson.Options, cancellationToken);
                foreach (ApprovalVerificationKey published in set?.Keys ?? [])
                {
                    if (published is { KeyType: "EC", Curve: "P-256", Algorithm: ApprovalAttestation.Algorithm } && !_keys.ContainsKey(published.KeyId))
                    {
                        _keys[published.KeyId] = ECDsa.Create(new ECParameters
                        {
                            Curve = ECCurve.NamedCurves.nistP256,
                            Q = new ECPoint { X = Base64Url.DecodeFromChars(published.X), Y = Base64Url.DecodeFromChars(published.Y) }
                        });
                    }
                }

                logger.LogInformation("Loaded {Count} approval verification keys from Api", _keys.Count);
            }
        }
        catch (HttpRequestException exception)
        {
            logger.LogWarning(exception, "Approval keys could not be loaded from Api");
        }
        finally
        {
            _refresh.Release();
        }

        return _keys.GetValueOrDefault(keyId);
    }

    public void Dispose()
    {
        foreach (ECDsa key in _keys.Values)
        {
            key.Dispose();
        }

        _refresh.Dispose();
    }
}

/// <summary>Runtime's start confirmation over HTTP; any failure to get an answer throws, so execution fails closed.</summary>
internal sealed class RuntimeStartConfirmation(HttpClient http) : IStartConfirmation
{
    public async Task<RemediationStartResponse> ConfirmAsync(RemediationStartRequest request, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await http.PostAsJsonAsync("/v1/remediation/start-confirmations", request, SentinelJson.Options, cancellationToken);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<RemediationStartResponse>(SentinelJson.Options, cancellationToken))!;
    }
}

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

/// <summary>Defence in depth: an adapter acts only under a capability for exactly its action, resource and scope.</summary>
internal static class CapabilityCheck
{
    public static AdapterResult? Refuse(Capability capability, string action, string resource, TimeProvider time)
    {
        CleanArchitecture.BuildingBlocks.Result authorized = capability.Authorizes(action, resource, capability.Scope, time.GetUtcNow());
        return authorized.IsSuccess ? null : new AdapterResult(AdapterOutcome.Rejected, authorized.Error.Description);
    }
}

/// <summary>
/// Pauses and resumes the reference reporting worker's batch (Phase 06). Pausing is compensated by
/// resuming. The worker's control token is a privileged credential, so only Executor holds it.
/// Policy keeps this action suggestion-only in the reference environment: responders run it.
/// </summary>
internal sealed class ReportingBatchAdapter(HttpClient http, TimeProvider time) : IRemediationAdapter
{
    public const string ControlHeader = "X-Reference-Control-Token";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public string ActionKind => "pause-batch";

    public IReadOnlyDictionary<string, string> CapabilityLimits(RemediationAction action) =>
        new Dictionary<string, string>(StringComparer.Ordinal) { ["operation"] = "stop-batch" };

    /// <summary>Reports whether the batch is active; a paused batch is the completed state of a pause.</summary>
    public async Task<TargetInspection> InspectAsync(RemediationAction action, string idempotencyKey, CancellationToken cancellationToken)
    {
        BatchStatus? status = await http.GetFromJsonAsync<BatchStatus>("/api/batch", Json, cancellationToken);
        bool active = status?.Active ?? false;
        return new TargetInspection(
            active ? "active" : "inactive",
            active ? OperationStatus.InProgress : OperationStatus.Completed,
            string.Create(CultureInfo.InvariantCulture, $"The batch is {(active ? "active" : "inactive")} with {status?.Connections ?? 0} Redis connections."));
    }

    public async Task<AdapterResult> ExecuteAsync(RemediationAction action, Capability capability, string idempotencyKey, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(capability);

        if (CapabilityCheck.Refuse(capability, ActionKind, action.TargetServiceId.Value, time) is { } refused)
        {
            return refused;
        }

        using HttpResponseMessage response = await http.PostAsync(new Uri("/_reference/batch/stop", UriKind.Relative), null, cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            return new AdapterResult(AdapterOutcome.Accepted, $"{action.TargetServiceId} batch paused.");
        }

        AdapterOutcome outcome = response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden or HttpStatusCode.NotFound ? AdapterOutcome.Rejected : AdapterOutcome.Unknown;
        return new AdapterResult(outcome, $"Pausing the batch answered {(int)response.StatusCode}.");
    }

    public async Task<bool> VerifyActionAsync(RemediationAction action, string idempotencyKey, CancellationToken cancellationToken) =>
        (await InspectAsync(action, idempotencyKey, cancellationToken)).Operation == OperationStatus.Completed;

    public async Task<AdapterResult> CompensateAsync(RemediationAction action, Capability capability, string idempotencyKey, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(capability);

        using HttpResponseMessage response = await http.PostAsync(new Uri("/_reference/batch/start", UriKind.Relative), JsonContent.Create(new { }), cancellationToken);
        return response.IsSuccessStatusCode
            ? new AdapterResult(AdapterOutcome.Accepted, $"{action.TargetServiceId} batch resumed.")
            : new AdapterResult(AdapterOutcome.Rejected, $"Resuming the batch answered {(int)response.StatusCode}.");
    }

    private sealed record BatchStatus(bool Active, int Connections);
}
