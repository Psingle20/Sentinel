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
