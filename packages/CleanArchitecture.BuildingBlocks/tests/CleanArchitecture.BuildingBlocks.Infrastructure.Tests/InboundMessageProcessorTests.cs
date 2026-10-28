using CleanArchitecture.BuildingBlocks.Infrastructure.Messaging;
using CleanArchitecture.BuildingBlocks.Integration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace CleanArchitecture.BuildingBlocks.Infrastructure.Tests;

public sealed class InboundMessageProcessorTests
{
    private sealed class ProcessorFixture : IDisposable
    {
        private readonly ServiceProvider _provider;

        public ProcessorFixture()
        {
            ServiceCollection services = new();
            services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
            services.AddSingleton(Inbox);
            services.AddSingleton<IInboxStore>(Inbox);
            services.AddSingleton(UnitOfWork);
            services.AddSingleton<IUnitOfWork>(UnitOfWork);
            services.AddSingleton(Handler);
            services.AddMessaging("billing").Consumes<OrderPlaced, RecordingHandler>("orders.order.placed", 1, "test.orders");
            _provider = services.BuildServiceProvider();
        }

        public InMemoryInbox Inbox { get; } = new();

        public CountingUnitOfWork UnitOfWork { get; } = new();

        public RecordingHandler Handler { get; } = new();

        public IInboundMessageProcessor Processor => _provider.GetRequiredService<IInboundMessageProcessor>();

        public void Dispose() => _provider.Dispose();
    }

    private static MessageEnvelope Envelope(string type = "orders.order.placed", int version = 1, string? payload = null) =>
        new(
            Guid.CreateVersion7(),
            type,
            version,
            DateTimeOffset.UtcNow,
            "orders",
            "flow-1",
            null,
            new Dictionary<string, string>(),
            payload ?? """{"orderId":"0192f0c4-5b6e-7a3c-9d2e-1f4a5b6c7d8e","channel":"webShop"}""");

    [Fact]
    public async Task A_handled_message_is_recorded_and_committed()
    {
        using ProcessorFixture fixture = new();
        MessageEnvelope envelope = Envelope();

        ProcessingResult result = await fixture.Processor.ProcessAsync(envelope, CancellationToken.None);

        result.Outcome.ShouldBe(ProcessingOutcome.Processed);
        fixture.Handler.Handled.ShouldHaveSingleItem().Channel.ShouldBe(OrderChannel.WebShop);
        fixture.Inbox.Recorded.ShouldContain(("billing", envelope.MessageId));
        fixture.UnitOfWork.Commits.ShouldBe(1);
    }

    [Fact]
    public async Task A_duplicate_is_not_handled_again()
    {
        using ProcessorFixture fixture = new();
        MessageEnvelope envelope = Envelope();
        await fixture.Processor.ProcessAsync(envelope, CancellationToken.None);

        ProcessingResult second = await fixture.Processor.ProcessAsync(envelope, CancellationToken.None);

        second.Outcome.ShouldBe(ProcessingOutcome.Duplicate);
        fixture.Handler.Handled.Count.ShouldBe(1);
        fixture.UnitOfWork.Commits.ShouldBe(1);
    }

    [Fact]
    public async Task A_handler_failure_is_not_committed()
    {
        using ProcessorFixture fixture = new();
        fixture.Handler.Behavior = _ => Result.Failure(Error.Conflict("Invoice.Exists", "An invoice already exists."));

        ProcessingResult result = await fixture.Processor.ProcessAsync(Envelope(), CancellationToken.None);

        result.Outcome.ShouldBe(ProcessingOutcome.Failed);
        result.Reason.ShouldBe("Invoice.Exists: An invoice already exists.");
        fixture.UnitOfWork.Commits.ShouldBe(0);
    }

    [Fact]
    public async Task A_handler_exception_is_reported_as_a_failure()
    {
        using ProcessorFixture fixture = new();
        fixture.Handler.Behavior = _ => throw new TimeoutException("database timed out");

        ProcessingResult result = await fixture.Processor.ProcessAsync(Envelope(), CancellationToken.None);

        result.Outcome.ShouldBe(ProcessingOutcome.Failed);
        result.Reason.ShouldBe("database timed out");
        fixture.UnitOfWork.Commits.ShouldBe(0);
    }

    [Theory]
    [InlineData("orders.order.cancelled", 1)]
    [InlineData("orders.order.placed", 2)]
    public async Task Unsupported_types_and_versions_are_rejected_without_handling(string type, int version)
    {
        using ProcessorFixture fixture = new();

        ProcessingResult result = await fixture.Processor.ProcessAsync(Envelope(type, version), CancellationToken.None);

        result.Outcome.ShouldBe(ProcessingOutcome.Rejected);
        fixture.Handler.Handled.ShouldBeEmpty();
        fixture.Inbox.Recorded.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_malformed_payload_is_rejected()
    {
        using ProcessorFixture fixture = new();

        ProcessingResult result = await fixture.Processor.ProcessAsync(Envelope(payload: "{not json"), CancellationToken.None);

        result.Outcome.ShouldBe(ProcessingOutcome.Rejected);
        result.Reason.ShouldStartWith("Malformed payload");
    }
}
