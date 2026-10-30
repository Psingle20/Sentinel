using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Sentinel.Contracts.Remediation;
using Sentinel.Executor.Application.Abstractions.Remediation;
using Sentinel.Executor.Domain.Capabilities;

namespace Sentinel.Executor.Infrastructure.Remediation;

/// <summary>
/// Quarantines a reference queue consumer's poison messages (Phase E01): the messages received at least the action's
/// minimum number of times, up to its maximum, leave the queue. Releasing them back is the compensation. The consumer's
/// control token is a privileged credential, so only Executor holds it. The queue's state is <c>poisoned</c> while such a
/// message is still waiting or in flight and <c>clear</c> once none is.
/// </summary>
internal sealed class ReferenceQueueQuarantineAdapter(HttpClient http, TimeProvider time) : IRemediationAdapter
{
    public const string ControlHeader = "X-Reference-Control-Token";
    public const string Poisoned = "poisoned";
    public const string Clear = "clear";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public string ActionKind => "quarantine-message";

    public IReadOnlyDictionary<string, string> CapabilityLimits(RemediationAction action)
    {
        QuarantineMessageAction quarantine = Expect(action);
        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["operation"] = "quarantine",
            ["queue"] = quarantine.Queue,
            ["minimumReceives"] = quarantine.MinimumReceives.ToString(CultureInfo.InvariantCulture),
            ["maxMessages"] = quarantine.MaxMessages.ToString(CultureInfo.InvariantCulture)
        };
    }

    public async Task<TargetInspection> InspectAsync(RemediationAction action, string idempotencyKey, CancellationToken cancellationToken)
    {
        QuarantineMessageAction quarantine = Expect(action);
        QueueStatus status = await http.GetFromJsonAsync<QueueStatus>("/api/queue", Json, cancellationToken)
            ?? throw new HttpRequestException("The queue reported no status.");
        Redelivered[] poisoned = [.. status.Redelivered.Where(message => message.Receives >= quarantine.MinimumReceives)];
        return new TargetInspection(
            poisoned.Length > 0 ? Poisoned : Clear,
            poisoned.Length == 0 && status.Quarantined > 0 ? OperationStatus.Completed : OperationStatus.NotFound,
            string.Create(CultureInfo.InvariantCulture, $"Queue {status.Queue}: {poisoned.Length} message(s) received at least {quarantine.MinimumReceives} times ({string.Join(", ", poisoned.Select(message => $"{message.Id} x{message.Receives}"))}), {status.Quarantined} quarantined, oldest message {status.OldestMessageAgeSeconds:F0} s."));
    }

    public async Task<AdapterResult> ExecuteAsync(RemediationAction action, Capability capability, string idempotencyKey, CancellationToken cancellationToken)
    {
        QuarantineMessageAction quarantine = Expect(action);
        ArgumentNullException.ThrowIfNull(capability);

        if (CapabilityCheck.Refuse(capability, ActionKind, action.TargetServiceId.Value, time) is { } refused)
        {
            return refused;
        }

        using HttpResponseMessage response = await http.PostAsJsonAsync(
            new Uri("/_reference/queue/quarantine", UriKind.Relative),
            new { minimumReceives = quarantine.MinimumReceives, maxMessages = quarantine.MaxMessages },
            Json,
            cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            Moved moved = (await response.Content.ReadFromJsonAsync<Moved>(Json, cancellationToken))!;
            return new AdapterResult(AdapterOutcome.Accepted, $"Quarantined {moved.Messages.Count} message(s) of {quarantine.Queue}: {string.Join(", ", moved.Messages)}.");
        }

        AdapterOutcome outcome = response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden or HttpStatusCode.NotFound ? AdapterOutcome.Rejected : AdapterOutcome.Unknown;
        return new AdapterResult(outcome, $"Quarantining answered {(int)response.StatusCode}.");
    }

    /// <summary>True once no message received the minimum number of times is still waiting or in flight.</summary>
    public async Task<bool> VerifyActionAsync(RemediationAction action, string idempotencyKey, CancellationToken cancellationToken) =>
        (await InspectAsync(action, idempotencyKey, cancellationToken)).ServingRevision == Clear;

    public async Task<AdapterResult> CompensateAsync(RemediationAction action, Capability capability, string idempotencyKey, CancellationToken cancellationToken)
    {
        QuarantineMessageAction quarantine = Expect(action);
        ArgumentNullException.ThrowIfNull(capability);

        using HttpResponseMessage response = await http.PostAsync(new Uri("/_reference/queue/release", UriKind.Relative), null, cancellationToken);
        return response.IsSuccessStatusCode
            ? new AdapterResult(AdapterOutcome.Accepted, $"Released the quarantined messages of {quarantine.Queue}.")
            : new AdapterResult(AdapterOutcome.Rejected, $"Releasing answered {(int)response.StatusCode}.");
    }

    private static QuarantineMessageAction Expect(RemediationAction action) =>
        action as QuarantineMessageAction ?? throw new ArgumentException($"Expected a quarantine-message action, not {action?.GetType().Name}.", nameof(action));

    private sealed record QueueStatus(string Queue, int Quarantined, double OldestMessageAgeSeconds, IReadOnlyList<Redelivered> Redelivered);

    private sealed record Redelivered(string Id, int Receives, string State);

    private sealed record Moved(IReadOnlyList<string> Messages);
}
