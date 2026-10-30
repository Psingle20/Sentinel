using System.Globalization;
using System.Text.Json;
using Amazon.Runtime;
using Amazon.SQS;
using Amazon.SQS.Model;
using Microsoft.Extensions.Logging;
using Sentinel.Contracts.Remediation;
using Sentinel.Executor.Application.Abstractions.Remediation;
using Sentinel.Executor.Domain.Capabilities;

namespace Sentinel.Executor.Infrastructure.Remediation;

/// <summary>
/// Quarantines a queue consumer's poison messages on SQS (Phase E01, Stage 2), the way an AWS operator would. For a
/// short window the queue's redrive policy dead-letters every message already received the action's minimum number of
/// times, so SQS itself moves the poison message to the dead-letter queue at its next receive; then the queue's own policy,
/// or the absence of one, is put back. Nothing is received from the queue: SQS has no peek, and a receive would count
/// against every message it touched. The queue's state is <c>poisoned</c> while its dead-letter queue is empty and
/// <c>clear</c> once it holds the quarantined messages; healthy messages never reach it, because they are processed long
/// before the minimum. Releasing the dead-lettered messages back is the compensation. Only queues on the configured
/// allow-list can be touched, and the action names neither an ARN nor an operation of its own. The pinned emulator
/// applies a changed policy only to a queue created without one (ADR-021).
/// </summary>
internal sealed class SqsQuarantineAdapter(IAmazonSQS sqs, SqsQuarantineOptions options, TimeProvider time, ILogger<SqsQuarantineAdapter> logger) : IRemediationAdapter
{
    public const string Poisoned = "poisoned";
    public const string Clear = "clear";

    private const string RedrivePolicy = "RedrivePolicy";
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);

    public string ActionKind => "quarantine-message";

    public IReadOnlyDictionary<string, string> CapabilityLimits(RemediationAction action)
    {
        QuarantineMessageAction quarantine = Expect(action);
        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["operation"] = "quarantine",
            ["queue"] = quarantine.Queue,
            ["minimumReceives"] = quarantine.MinimumReceives.ToString(CultureInfo.InvariantCulture),
            ["maxMessages"] = quarantine.MaxMessages.ToString(CultureInfo.InvariantCulture),
            ["window"] = options.Window.ToString("c", CultureInfo.InvariantCulture)
        };
    }

    public async Task<TargetInspection> InspectAsync(RemediationAction action, string idempotencyKey, CancellationToken cancellationToken)
    {
        QuarantineMessageAction quarantine = Expect(action);
        if (!Allowed(quarantine.Queue))
        {
            return new TargetInspection(null, OperationStatus.NotFound, $"Queue {quarantine.Queue} is not on Executor's allow-list.");
        }

        try
        {
            int quarantined = await DepthAsync(await UrlAsync(DeadLetterQueue(quarantine.Queue), cancellationToken), cancellationToken);
            return new TargetInspection(
                quarantined > 0 ? Clear : Poisoned,
                quarantined > 0 ? OperationStatus.Completed : OperationStatus.NotFound,
                string.Create(CultureInfo.InvariantCulture, $"Queue {quarantine.Queue}: {quarantined} message(s) in {DeadLetterQueue(quarantine.Queue)}."));
        }
        catch (Exception exception) when (exception is AmazonServiceException or AmazonClientException or HttpRequestException or TimeoutException)
        {
            return new TargetInspection(null, OperationStatus.Failed, $"Inspecting {quarantine.Queue} failed: {exception.Message}");
        }
    }

    public async Task<AdapterResult> ExecuteAsync(RemediationAction action, Capability capability, string idempotencyKey, CancellationToken cancellationToken)
    {
        QuarantineMessageAction quarantine = Expect(action);
        ArgumentNullException.ThrowIfNull(capability);

        if (CapabilityCheck.Refuse(capability, ActionKind, action.TargetServiceId.Value, time) is { } refused)
        {
            return refused;
        }

        if (!Allowed(quarantine.Queue))
        {
            return new AdapterResult(AdapterOutcome.Rejected, $"Queue {quarantine.Queue} is not on Executor's allow-list.");
        }

        string? original = null;
        string queue = string.Empty;
        try
        {
            queue = await UrlAsync(quarantine.Queue, cancellationToken);
            string deadLetters = await UrlAsync(DeadLetterQueue(quarantine.Queue), cancellationToken);
            string deadLetterArn = (await AttributesAsync(deadLetters, ["QueueArn"], cancellationToken))["QueueArn"];
            original = (await AttributesAsync(queue, [RedrivePolicy], cancellationToken)).GetValueOrDefault(RedrivePolicy);
            int before = await DepthAsync(deadLetters, cancellationToken);

            await SetRedriveAsync(queue, Redrive(deadLetterArn, quarantine.MinimumReceives), cancellationToken);
            DateTimeOffset closes = time.GetUtcNow() + options.Window;
            int moved = 0;
            while (time.GetUtcNow() < closes && moved < quarantine.MaxMessages)
            {
                await Task.Delay(PollInterval, time, cancellationToken);
                moved = await DepthAsync(deadLetters, cancellationToken) - before;
                if (moved > 0)
                {
                    break;
                }
            }

            return moved > 0
                ? new AdapterResult(AdapterOutcome.Accepted, $"Dead-lettered {moved} message(s) of {quarantine.Queue} received at least {quarantine.MinimumReceives} times.")
                : new AdapterResult(AdapterOutcome.Unknown, $"No message of {quarantine.Queue} was received {quarantine.MinimumReceives} times within {options.Window.TotalSeconds:F0} s.");
        }
        catch (Exception exception) when (exception is AmazonServiceException or AmazonClientException or HttpRequestException or TimeoutException)
        {
            return new AdapterResult(AdapterOutcome.Unknown, $"Quarantining {quarantine.Queue} failed: {exception.Message}");
        }
        finally
        {
            if (queue.Length > 0)
            {
                await RestoreAsync(queue, original, quarantine.Queue);
            }
        }
    }

    /// <summary>True once the dead-letter queue holds the quarantined messages.</summary>
    public async Task<bool> VerifyActionAsync(RemediationAction action, string idempotencyKey, CancellationToken cancellationToken) =>
        (await InspectAsync(action, idempotencyKey, cancellationToken)).ServingRevision == Clear;

    public async Task<AdapterResult> CompensateAsync(RemediationAction action, Capability capability, string idempotencyKey, CancellationToken cancellationToken)
    {
        QuarantineMessageAction quarantine = Expect(action);
        ArgumentNullException.ThrowIfNull(capability);

        if (!Allowed(quarantine.Queue))
        {
            return new AdapterResult(AdapterOutcome.Rejected, $"Queue {quarantine.Queue} is not on Executor's allow-list.");
        }

        try
        {
            string queue = await UrlAsync(quarantine.Queue, cancellationToken);
            string deadLetters = await UrlAsync(DeadLetterQueue(quarantine.Queue), cancellationToken);
            int released = 0;
            while ((await sqs.ReceiveMessageAsync(new ReceiveMessageRequest { QueueUrl = deadLetters, MaxNumberOfMessages = 10, WaitTimeSeconds = 1 }, cancellationToken)).Messages is { Count: > 0 } messages)
            {
                foreach (Message message in messages)
                {
                    await sqs.SendMessageAsync(new SendMessageRequest { QueueUrl = queue, MessageBody = message.Body }, cancellationToken);
                    await sqs.DeleteMessageAsync(deadLetters, message.ReceiptHandle, cancellationToken);
                    released++;
                }
            }

            return new AdapterResult(AdapterOutcome.Accepted, $"Released {released} quarantined message(s) back to {quarantine.Queue}.");
        }
        catch (Exception exception) when (exception is AmazonServiceException or AmazonClientException or HttpRequestException or TimeoutException)
        {
            return new AdapterResult(AdapterOutcome.Rejected, $"Releasing {quarantine.Queue} failed: {exception.Message}");
        }
    }

    private bool Allowed(string queue) => options.Queues.Contains(queue, StringComparer.Ordinal);

    private string DeadLetterQueue(string queue) => queue + options.DeadLetterSuffix;

    private async Task<string> UrlAsync(string queue, CancellationToken cancellationToken) =>
        (await sqs.GetQueueUrlAsync(queue, cancellationToken)).QueueUrl;

    private async Task<Dictionary<string, string>> AttributesAsync(string queue, List<string> names, CancellationToken cancellationToken) =>
        (await sqs.GetQueueAttributesAsync(new GetQueueAttributesRequest { QueueUrl = queue, AttributeNames = names }, cancellationToken)).Attributes ?? [];

    private async Task<int> DepthAsync(string queue, CancellationToken cancellationToken) =>
        (await AttributesAsync(queue, ["ApproximateNumberOfMessages", "ApproximateNumberOfMessagesNotVisible"], cancellationToken))
            .Values.Sum(value => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int count) ? count : 0);

    // An empty policy removes it, as from a queue that had none of its own.
    private async Task SetRedriveAsync(string queue, string? policy, CancellationToken cancellationToken) =>
        await sqs.SetQueueAttributesAsync(new SetQueueAttributesRequest { QueueUrl = queue, Attributes = new Dictionary<string, string> { [RedrivePolicy] = policy ?? string.Empty } }, cancellationToken);

    // The window always closes, even when the action is cancelled or failed half-way.
    private async Task RestoreAsync(string queue, string? original, string name)
    {
        try
        {
            await SetRedriveAsync(queue, original, CancellationToken.None);
        }
        catch (Exception exception) when (exception is AmazonServiceException or AmazonClientException or HttpRequestException or TimeoutException)
        {
            logger.LogError(exception, "The redrive policy of {Queue} could not be put back; it still dead-letters messages after their minimum receives", name);
        }
    }

    private static string Redrive(string deadLetterArn, int maxReceiveCount) =>
        JsonSerializer.Serialize(new Dictionary<string, string> { ["deadLetterTargetArn"] = deadLetterArn, ["maxReceiveCount"] = maxReceiveCount.ToString(CultureInfo.InvariantCulture) });

    private static QuarantineMessageAction Expect(RemediationAction action) =>
        action as QuarantineMessageAction ?? throw new ArgumentException($"Expected a quarantine-message action, not {action?.GetType().Name}.", nameof(action));
}
