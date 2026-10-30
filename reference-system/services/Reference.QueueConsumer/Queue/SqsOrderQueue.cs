using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using Amazon.Runtime;
using Amazon.SQS;
using Amazon.SQS.Model;

namespace Reference.QueueConsumer.Queue;

/// <summary>
/// The orders queue as an SQS queue, for the AWS reference environment (Phase E01). A received message stays invisible
/// for the visibility timeout and returns on its own unless it is deleted; SQS counts every receive itself; quarantine
/// moves a message to the dead-letter queue. SQS reports neither a message's age nor its receive count without
/// receiving it, so the status takes both from the messages this consumer holds, which, as it takes the oldest first,
/// include the oldest one. The booking ledger is the task's own.
/// </summary>
public sealed class SqsOrderQueue(IAmazonSQS sqs, SqsQueueOptions options, TimeProvider time) : IOrderQueue
{
    private const string ReceiveCount = "ApproximateReceiveCount";
    private const string SentTimestamp = "SentTimestamp";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly ConcurrentDictionary<string, InFlightOrder> _inFlight = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, int> _bookings = new(StringComparer.Ordinal);
    private string? _queueUrl;
    private string? _deadLetterUrl;

    public Task EnqueueAsync(OrderMessage message, CancellationToken cancellationToken) =>
        CallAsync(async () => await sqs.SendMessageAsync(new SendMessageRequest { QueueUrl = await QueueAsync(cancellationToken), MessageBody = JsonSerializer.Serialize(message, Json) }, cancellationToken));

    public Task<ReceivedOrder?> ReceiveAsync(CancellationToken cancellationToken) => CallAsync(async () =>
    {
        ReceiveMessageResponse response = await sqs.ReceiveMessageAsync(Receive(await QueueAsync(cancellationToken), 1), cancellationToken);
        if (response.Messages is not [Message message, ..])
        {
            return null;
        }

        OrderMessage order = JsonSerializer.Deserialize<OrderMessage>(message.Body, Json)!;
        int receive = ReceivesOf(message);
        _inFlight[message.ReceiptHandle] = new InFlightOrder(order.Id, SentAt(message) ?? order.EnqueuedAt, receive);
        return new ReceivedOrder(order, message.ReceiptHandle, receive);
    });

    public Task CompleteAsync(ReceivedOrder received, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(received);

        return CallAsync(async () =>
        {
            await sqs.DeleteMessageAsync(await QueueAsync(cancellationToken), received.Handle, cancellationToken);
            _inFlight.TryRemove(received.Handle, out _);
        });
    }

    public Task<int> BookAsync(OrderMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        return Task.FromResult(_bookings.AddOrUpdate(message.OrderId, 1, (_, booked) => booked + 1));
    }

    public Task ReturnAsync(ReceivedOrder received, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(received);

        return CallAsync(async () =>
        {
            _inFlight.TryRemove(received.Handle, out _);
            await sqs.ChangeMessageVisibilityAsync(new ChangeMessageVisibilityRequest { QueueUrl = await QueueAsync(cancellationToken), ReceiptHandle = received.Handle, VisibilityTimeout = 0 }, cancellationToken);
        });
    }

    public void Abandon(ReceivedOrder received)
    {
        ArgumentNullException.ThrowIfNull(received);

        _inFlight.TryRemove(received.Handle, out _);
    }

    /// <summary>SQS returns an abandoned message itself, once its visibility timeout ends.</summary>
    public Task ReclaimAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>
    /// Receives the waiting messages and moves those already received at least <c>MinimumReceives</c> times to the
    /// dead-letter queue, at most <c>MaxMessages</c>; the rest become visible again at once.
    /// </summary>
    public Task<QuarantineResult> QuarantineAsync(QuarantineRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return CallAsync(async () =>
        {
            string queue = await QueueAsync(cancellationToken);
            string deadLetters = await DeadLetterQueueAsync(cancellationToken);
            var moved = new List<string>();
            ReceiveMessageResponse response = await sqs.ReceiveMessageAsync(Receive(queue, 10), cancellationToken);
            foreach (Message message in response.Messages ?? [])
            {
                // This receive counts too, so a message received n times before now reads n + 1.
                if (moved.Count < request.MaxMessages && ReceivesOf(message) - 1 >= request.MinimumReceives)
                {
                    await sqs.SendMessageAsync(new SendMessageRequest { QueueUrl = deadLetters, MessageBody = message.Body }, cancellationToken);
                    await sqs.DeleteMessageAsync(queue, message.ReceiptHandle, cancellationToken);
                    moved.Add(JsonSerializer.Deserialize<OrderMessage>(message.Body, Json)!.Id);
                }
                else
                {
                    await sqs.ChangeMessageVisibilityAsync(new ChangeMessageVisibilityRequest { QueueUrl = queue, ReceiptHandle = message.ReceiptHandle, VisibilityTimeout = 0 }, cancellationToken);
                }
            }

            return new QuarantineResult(moved);
        });
    }

    public Task<QuarantineResult> ReleaseAsync(CancellationToken cancellationToken) => CallAsync(async () =>
    {
        string queue = await QueueAsync(cancellationToken);
        string deadLetters = await DeadLetterQueueAsync(cancellationToken);
        var released = new List<string>();
        while ((await sqs.ReceiveMessageAsync(Receive(deadLetters, 10), cancellationToken)).Messages is { Count: > 0 } messages)
        {
            foreach (Message message in messages)
            {
                await sqs.SendMessageAsync(new SendMessageRequest { QueueUrl = queue, MessageBody = message.Body }, cancellationToken);
                await sqs.DeleteMessageAsync(deadLetters, message.ReceiptHandle, cancellationToken);
                released.Add(JsonSerializer.Deserialize<OrderMessage>(message.Body, Json)!.Id);
            }
        }

        return new QuarantineResult(released);
    });

    public Task ResetAsync(CancellationToken cancellationToken) => CallAsync(async () =>
    {
        await sqs.PurgeQueueAsync(await QueueAsync(cancellationToken), cancellationToken);
        await sqs.PurgeQueueAsync(await DeadLetterQueueAsync(cancellationToken), cancellationToken);
        _inFlight.Clear();
        _bookings.Clear();
    });

    public Task<QueueStatus> StatusAsync(CancellationToken cancellationToken) => CallAsync(async () =>
    {
        Dictionary<string, string> queue = await AttributesAsync(await QueueAsync(cancellationToken), cancellationToken);
        Dictionary<string, string> deadLetters = await AttributesAsync(await DeadLetterQueueAsync(cancellationToken), cancellationToken);
        DateTimeOffset now = time.GetUtcNow();
        InFlightOrder[] held = [.. _inFlight.Values];
        MessageView[] redelivered = [.. held.Where(order => order.Receive > 1).OrderByDescending(order => order.Receive).Select(order => new MessageView(order.Id, order.Receive, "in-flight"))];
        return new QueueStatus(
            IOrderQueue.Name,
            Count(queue, "ApproximateNumberOfMessages"),
            Count(queue, "ApproximateNumberOfMessagesNotVisible"),
            Count(deadLetters, "ApproximateNumberOfMessages"),
            held.Select(order => (now - order.SentAt).TotalSeconds).DefaultIfEmpty(0).Max(),
            held.Select(order => order.Receive).DefaultIfEmpty(0).Max(),
            redelivered);
    });

    public Task<MessageView> MessageAsync(string id, CancellationToken cancellationToken) =>
        Task.FromResult(_inFlight.Values.FirstOrDefault(order => order.Id == id) is { } held
            ? new MessageView(id, held.Receive, "in-flight")
            : new MessageView(id, 0, "unknown"));

    private ReceiveMessageRequest Receive(string queue, int count) => new()
    {
        QueueUrl = queue,
        MaxNumberOfMessages = count,
        WaitTimeSeconds = 1,
        VisibilityTimeout = options.VisibilityTimeoutSeconds,
        MessageSystemAttributeNames = [ReceiveCount, SentTimestamp]
    };

    private async Task<string> QueueAsync(CancellationToken cancellationToken) =>
        _queueUrl ??= (await sqs.GetQueueUrlAsync(options.QueueName, cancellationToken)).QueueUrl;

    private async Task<string> DeadLetterQueueAsync(CancellationToken cancellationToken) =>
        _deadLetterUrl ??= (await sqs.GetQueueUrlAsync(options.DeadLetterQueueName, cancellationToken)).QueueUrl;

    private async Task<Dictionary<string, string>> AttributesAsync(string queue, CancellationToken cancellationToken) =>
        (await sqs.GetQueueAttributesAsync(new GetQueueAttributesRequest { QueueUrl = queue, AttributeNames = ["ApproximateNumberOfMessages", "ApproximateNumberOfMessagesNotVisible"] }, cancellationToken)).Attributes ?? [];

    private static int Count(Dictionary<string, string> attributes, string name) =>
        attributes.TryGetValue(name, out string? value) && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int count) ? count : 0;

    private static int ReceivesOf(Message message) =>
        message.Attributes is { } attributes && attributes.TryGetValue(ReceiveCount, out string? value) && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int count) ? count : 1;

    private static DateTimeOffset? SentAt(Message message) =>
        message.Attributes is { } attributes && attributes.TryGetValue(SentTimestamp, out string? value) && long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long milliseconds)
            ? DateTimeOffset.FromUnixTimeMilliseconds(milliseconds)
            : null;

    private static async Task CallAsync(Func<Task> call)
    {
        try
        {
            await call();
        }
        catch (Exception exception) when (exception is AmazonServiceException or AmazonClientException or HttpRequestException)
        {
            throw new QueueUnavailableException("The orders queue in SQS is unavailable.", exception);
        }
    }

    private static async Task<T> CallAsync<T>(Func<Task<T>> call)
    {
        try
        {
            return await call();
        }
        catch (Exception exception) when (exception is AmazonServiceException or AmazonClientException or HttpRequestException)
        {
            throw new QueueUnavailableException("The orders queue in SQS is unavailable.", exception);
        }
    }
}
