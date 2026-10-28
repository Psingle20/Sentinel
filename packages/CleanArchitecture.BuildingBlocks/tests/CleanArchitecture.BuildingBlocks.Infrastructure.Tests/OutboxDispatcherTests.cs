using CleanArchitecture.BuildingBlocks.Infrastructure.Messaging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;

namespace CleanArchitecture.BuildingBlocks.Infrastructure.Tests;

public sealed class OutboxDispatcherTests
{
    private static readonly OutboxOptions Options = new()
    {
        InitialRetryDelay = TimeSpan.FromSeconds(1),
        MaxRetryDelay = TimeSpan.FromSeconds(30)
    };

    private static OutboxRecord Record(int attempts = 0) =>
        new(
            new MessageEnvelope(Guid.CreateVersion7(), "orders.order.placed", 1, DateTimeOffset.UtcNow, "orders", null, null, new Dictionary<string, string>(), "{}"),
            "test.orders",
            attempts);

    private static OutboxDispatcher Dispatcher(InMemoryOutboxStore store, RecordingPublisher publisher) =>
        new(store, publisher, new MessagingStartupGate(), Microsoft.Extensions.Options.Options.Create(Options), TimeProvider.System, NullLogger<OutboxDispatcher>.Instance);

    [Fact]
    public async Task Due_records_are_published_and_marked()
    {
        InMemoryOutboxStore store = new();
        RecordingPublisher publisher = new();
        OutboxRecord first = Record();
        OutboxRecord second = Record();
        store.Due.AddRange([first, second]);

        int dispatched = await Dispatcher(store, publisher).DispatchBatchAsync(CancellationToken.None);

        dispatched.ShouldBe(2);
        publisher.Sent.Select(sent => sent.Envelope.MessageId).ShouldBe([first.Envelope.MessageId, second.Envelope.MessageId]);
        store.Published.ShouldBe([first.Envelope.MessageId, second.Envelope.MessageId]);
    }

    [Fact]
    public async Task A_publish_failure_schedules_a_retry_instead_of_dropping_the_record()
    {
        InMemoryOutboxStore store = new();
        RecordingPublisher publisher = new() { FailWith = new InvalidOperationException("redis unavailable") };
        OutboxRecord record = Record(attempts: 2);
        store.Due.Add(record);

        await Dispatcher(store, publisher).DispatchBatchAsync(CancellationToken.None);

        store.Published.ShouldBeEmpty();
        var failure = store.Failures.ShouldHaveSingleItem();
        failure.MessageId.ShouldBe(record.Envelope.MessageId);
        failure.RetryAfter.ShouldBe(TimeSpan.FromSeconds(4));
        failure.Error.ShouldBe("redis unavailable");
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(3, 4)]
    [InlineData(5, 16)]
    [InlineData(6, 30)]
    [InlineData(50, 30)]
    public void Retry_delay_doubles_per_attempt_up_to_the_cap(int attempt, int expectedSeconds)
    {
        OutboxDispatcher.RetryDelay(attempt, Options).ShouldBe(TimeSpan.FromSeconds(expectedSeconds));
    }
}
