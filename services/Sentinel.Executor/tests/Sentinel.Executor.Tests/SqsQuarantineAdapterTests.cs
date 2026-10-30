using System.Collections.Concurrent;
using System.Globalization;
using Amazon.SQS;
using Amazon.SQS.Model;
using Microsoft.Extensions.Logging.Abstractions;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Contracts.Remediation;
using Sentinel.Executor.Application.Abstractions.Remediation;
using Sentinel.Executor.Domain.Capabilities;
using Sentinel.Executor.Infrastructure.Remediation;
using Shouldly;

namespace Sentinel.Executor.Tests;

/// <summary>
/// The SQS quarantine adapter against the pinned emulator (Phase E01, Stage 2): beside a six-worker consumer that keeps
/// failing on one message and processes the rest, the adapter's redrive window lets SQS dead-letter that message and
/// nothing else, and the queue is left without a redrive policy again, as it was; the queue is then verified clear and
/// compensation releases the message. A window with nothing received often enough moves nothing, and a queue off the
/// allow-list is never touched. Each queue is created without a redrive policy of its own, as the AWS environment's is.
/// </summary>
[Trait("Category", "Emulator")]
public sealed class SqsQuarantineAdapterTests(SqsEmulator emulator) : IClassFixture<SqsEmulator>
{
    private const string Poison = """{"id":"msg-poison-1","orderId":"ord-poison-1","amount":"NaN"}""";

    [Fact]
    public async Task A_message_failing_on_every_receive_of_a_busy_consumer_is_moved_to_the_dead_letter_queue_and_nothing_else()
    {
        using AmazonSQSClient sqs = emulator.Sqs();
        string name = $"orders-{Guid.NewGuid().ToString("N")[..8]}";
        (string queue, string deadLetters) = await CreateAsync(sqs, name);
        await sqs.SendMessageAsync(new SendMessageRequest { QueueUrl = queue, MessageBody = Poison });

        var receives = new ConcurrentBag<int>();
        using var consumer = new CancellationTokenSource();
        Task[] workers = [.. Enumerable.Range(0, 6).Select(_ => Task.Run(() => ConsumeAsync(sqs, queue, receives, consumer.Token)))];
        while (receives.Count < 3)
        {
            await Task.Delay(100);
        }

        SqsQuarantineAdapter adapter = Adapter(sqs, name, TimeSpan.FromSeconds(30));
        QuarantineMessageAction action = Quarantine(name);
        (await adapter.InspectAsync(action, "key", CancellationToken.None)).ServingRevision.ShouldBe(SqsQuarantineAdapter.Poisoned);
        for (int order = 0; order < 5; order++)
        {
            await sqs.SendMessageAsync(new SendMessageRequest { QueueUrl = queue, MessageBody = $$"""{"id":"msg-{{order}}","orderId":"ord-{{order}}","amount":"10.00"}""" });
        }

        AdapterResult result = await adapter.ExecuteAsync(action, CapabilityFor(adapter, action, "quarantine-message"), "key", CancellationToken.None);
        await consumer.CancelAsync();
        await Task.WhenAll(workers);

        result.Outcome.ShouldBe(AdapterOutcome.Accepted, result.Detail);
        result.Detail.ShouldContain("Dead-lettered 1 message(s)");
        (await adapter.VerifyActionAsync(action, "key", CancellationToken.None)).ShouldBeTrue();
        (await Attributes(sqs, deadLetters, "ApproximateNumberOfMessages"))["ApproximateNumberOfMessages"].ShouldBe("1", "only the poison message is quarantined");
        (await Attributes(sqs, queue, "RedrivePolicy")).GetValueOrDefault("RedrivePolicy").ShouldBeNullOrEmpty("the queue has no redrive policy again");

        AdapterResult released = await adapter.CompensateAsync(action, CapabilityFor(adapter, action, "quarantine-message"), "key:compensate", CancellationToken.None);
        released.Outcome.ShouldBe(AdapterOutcome.Accepted);
        released.Detail.ShouldContain("Released 1");
        (await Attributes(sqs, deadLetters, "ApproximateNumberOfMessages"))["ApproximateNumberOfMessages"].ShouldBe("0");
        (await adapter.InspectAsync(action, "key", CancellationToken.None)).ServingRevision.ShouldBe(SqsQuarantineAdapter.Poisoned);
    }

    [Fact]
    public async Task A_window_without_a_message_received_often_enough_moves_nothing_and_leaves_the_queue_as_it_was()
    {
        using AmazonSQSClient sqs = emulator.Sqs();
        string name = $"orders-{Guid.NewGuid().ToString("N")[..8]}";
        (string queue, string deadLetters) = await CreateAsync(sqs, name);
        await sqs.SendMessageAsync(new SendMessageRequest { QueueUrl = queue, MessageBody = """{"id":"msg-1","orderId":"ord-1","amount":"10.00"}""" });

        SqsQuarantineAdapter adapter = Adapter(sqs, name, TimeSpan.FromSeconds(3));
        QuarantineMessageAction action = Quarantine(name);
        AdapterResult result = await adapter.ExecuteAsync(action, CapabilityFor(adapter, action, "quarantine-message"), "key", CancellationToken.None);

        result.Outcome.ShouldBe(AdapterOutcome.Unknown, result.Detail);
        (await Attributes(sqs, deadLetters, "ApproximateNumberOfMessages"))["ApproximateNumberOfMessages"].ShouldBe("0");
        ReceiveMessageResponse back = await sqs.ReceiveMessageAsync(new ReceiveMessageRequest { QueueUrl = queue, MaxNumberOfMessages = 1, WaitTimeSeconds = 5 });
        back.Messages.ShouldNotBeNull().ShouldHaveSingleItem("the healthy message is still on its queue");
    }

    [Fact]
    public async Task A_queue_off_the_allow_list_or_a_capability_for_another_action_moves_nothing()
    {
        using AmazonSQSClient sqs = emulator.Sqs();
        string name = $"orders-{Guid.NewGuid().ToString("N")[..8]}";
        (string queue, string deadLetters) = await CreateAsync(sqs, name);
        await sqs.SendMessageAsync(new SendMessageRequest { QueueUrl = queue, MessageBody = Poison });

        SqsQuarantineAdapter elsewhere = Adapter(sqs, "payments", TimeSpan.FromSeconds(3));
        QuarantineMessageAction action = Quarantine(name);
        (await elsewhere.ExecuteAsync(action, CapabilityFor(elsewhere, action, "quarantine-message"), "key", CancellationToken.None)).Outcome.ShouldBe(AdapterOutcome.Rejected);

        SqsQuarantineAdapter adapter = Adapter(sqs, name, TimeSpan.FromSeconds(3));
        (await adapter.ExecuteAsync(action, CapabilityFor(adapter, action, "rollback"), "key", CancellationToken.None)).Outcome.ShouldBe(AdapterOutcome.Rejected);
        (await Attributes(sqs, queue, "ApproximateNumberOfMessages"))["ApproximateNumberOfMessages"].ShouldBe("1", "nothing was received");
        (await Attributes(sqs, deadLetters, "ApproximateNumberOfMessages"))["ApproximateNumberOfMessages"].ShouldBe("0");
    }

    private static async Task<(string Queue, string DeadLetters)> CreateAsync(AmazonSQSClient sqs, string name)
    {
        string deadLetters = (await sqs.CreateQueueAsync(new CreateQueueRequest { QueueName = $"{name}-dlq" })).QueueUrl;
        string queue = (await sqs.CreateQueueAsync(new CreateQueueRequest { QueueName = name })).QueueUrl;
        return (queue, deadLetters);
    }

    // One of the consumer's workers, as the reference consumer's: it processes a healthy order, and on the poison message
    // it fails, holds it for its retry delay, hands it back and receives again at once.
    private static async Task ConsumeAsync(AmazonSQSClient sqs, string queue, ConcurrentBag<int> poisonReceives, CancellationToken stopping)
    {
        while (!stopping.IsCancellationRequested)
        {
            ReceiveMessageResponse received = await sqs.ReceiveMessageAsync(new ReceiveMessageRequest { QueueUrl = queue, MaxNumberOfMessages = 1, WaitTimeSeconds = 1, MessageSystemAttributeNames = ["ApproximateReceiveCount"] }, CancellationToken.None);
            if (received.Messages is not [Message message, ..])
            {
                await Task.Delay(TimeSpan.FromMilliseconds(100), CancellationToken.None);
                continue;
            }

            if (message.Body != Poison)
            {
                await sqs.DeleteMessageAsync(queue, message.ReceiptHandle, CancellationToken.None);
                continue;
            }

            poisonReceives.Add(int.Parse(message.Attributes["ApproximateReceiveCount"], CultureInfo.InvariantCulture));
            await Task.Delay(TimeSpan.FromSeconds(2), CancellationToken.None);
            await sqs.ChangeMessageVisibilityAsync(new ChangeMessageVisibilityRequest { QueueUrl = queue, ReceiptHandle = message.ReceiptHandle, VisibilityTimeout = 0 }, CancellationToken.None);
        }
    }

    private static async Task<Dictionary<string, string>> Attributes(AmazonSQSClient sqs, string queue, string name) =>
        (await sqs.GetQueueAttributesAsync(new GetQueueAttributesRequest { QueueUrl = queue, AttributeNames = [name] })).Attributes ?? [];

    private SqsQuarantineAdapter Adapter(AmazonSQSClient sqs, string allowed, TimeSpan window) => new(
        sqs,
        new SqsQuarantineOptions { ServiceUrl = emulator.Endpoint, AccessKey = SqsEmulator.Account, SecretKey = SqsEmulator.Secret, Queues = [allowed], Window = window },
        TimeProvider.System,
        NullLogger<SqsQuarantineAdapter>.Instance);

    private static QuarantineMessageAction Quarantine(string queue) => new()
    {
        ActionId = ActionId.New(),
        TargetServiceId = ServiceId.From("queue-consumer"),
        Queue = queue,
        MinimumReceives = 3,
        MaxMessages = 5,
        Reason = "It fails on every receive."
    };

    private static Capability CapabilityFor(SqsQuarantineAdapter adapter, QuarantineMessageAction action, string kind) => Capability.Issue(
        Guid.NewGuid(),
        kind,
        "queue-consumer",
        TenantId.From("acme"),
        EnvironmentId.From("reference"),
        adapter.CapabilityLimits(action),
        DateTimeOffset.UtcNow,
        TimeSpan.FromMinutes(1));
}
