using System.Diagnostics;
using CleanArchitecture.BuildingBlocks.Infrastructure.Messaging;
using CleanArchitecture.BuildingBlocks.Integration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Shouldly;

namespace CleanArchitecture.BuildingBlocks.Infrastructure.Tests;

public sealed class OutboxTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 28, 9, 30, 0, TimeSpan.Zero);

    private sealed class OutboxFixture : IDisposable
    {
        private readonly ServiceProvider _provider;
        private readonly IServiceScope _scope;

        public OutboxFixture()
        {
            ServiceCollection services = new();
            services.AddSingleton<TimeProvider>(new FakeTimeProvider(Now));
            services.AddMessaging("orders").Produces<OrderPlaced>("orders.order.placed", 1, "test.orders");
            _provider = services.BuildServiceProvider();
            _scope = _provider.CreateScope();
        }

        public Outbox Outbox => _scope.ServiceProvider.GetRequiredService<Outbox>();

        public MessageContextAccessor Context => _scope.ServiceProvider.GetRequiredService<MessageContextAccessor>();

        public void Dispose()
        {
            _scope.Dispose();
            _provider.Dispose();
        }
    }

    [Fact]
    public void A_new_flow_gets_its_own_correlation_and_no_causation()
    {
        using OutboxFixture fixture = new();
        Outbox outbox = fixture.Outbox;

        outbox.Enqueue(new OrderPlaced(Guid.NewGuid(), OrderChannel.WebShop));

        OutboxRecord record = outbox.DequeuePending().ShouldHaveSingleItem();
        record.Stream.ShouldBe("test.orders");
        record.Envelope.MessageType.ShouldBe("orders.order.placed");
        record.Envelope.SchemaVersion.ShouldBe(1);
        record.Envelope.Producer.ShouldBe("orders");
        record.Envelope.OccurredAt.ShouldBe(Now);
        record.Envelope.MessageId.Version.ShouldBe(7);
        record.Envelope.CorrelationId.ShouldBe(record.Envelope.MessageId.ToString());
        record.Envelope.CausationId.ShouldBeNull();
    }

    [Fact]
    public void Messages_sent_while_handling_inherit_correlation_causation_and_headers()
    {
        using OutboxFixture fixture = new();
        Outbox outbox = fixture.Outbox;
        MessageContextAccessor context = fixture.Context;
        var inbound = new MessageEnvelope(
            Guid.CreateVersion7(), "billing.invoice.requested", 1, Now, "billing", "flow-42", null,
            new Dictionary<string, string> { ["tenant"] = "acme", ["traceparent"] = "00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-01" },
            "{}");
        context.Current = inbound;

        outbox.Enqueue(new OrderPlaced(Guid.NewGuid(), OrderChannel.WebShop));

        MessageEnvelope envelope = outbox.DequeuePending().ShouldHaveSingleItem().Envelope;
        envelope.CorrelationId.ShouldBe("flow-42");
        envelope.CausationId.ShouldBe(inbound.MessageId.ToString());
        envelope.Headers["tenant"].ShouldBe("acme");
        envelope.Headers.ShouldNotContainKey("traceparent");
    }

    [Fact]
    public void Explicit_metadata_overrides_inherited_values()
    {
        using OutboxFixture fixture = new();
        Outbox outbox = fixture.Outbox;

        outbox.Enqueue(
            new OrderPlaced(Guid.NewGuid(), OrderChannel.PhoneOrder),
            new MessageMetadata("flow-7", "cause-1", new Dictionary<string, string> { ["tenant"] = "globex" }));

        MessageEnvelope envelope = outbox.DequeuePending().ShouldHaveSingleItem().Envelope;
        envelope.CorrelationId.ShouldBe("flow-7");
        envelope.CausationId.ShouldBe("cause-1");
        envelope.Headers["tenant"].ShouldBe("globex");
    }

    [Fact]
    public void The_current_trace_context_travels_in_the_headers()
    {
        using OutboxFixture fixture = new();
        Outbox outbox = fixture.Outbox;
        using var listener = new ActivityListener
        {
            ShouldListenTo = _ => true,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded
        };
        ActivitySource.AddActivityListener(listener);
        using var source = new ActivitySource("outbox-tests");
        using Activity? activity = source.StartActivity("handle");

        outbox.Enqueue(new OrderPlaced(Guid.NewGuid(), OrderChannel.WebShop));

        MessageEnvelope envelope = outbox.DequeuePending().ShouldHaveSingleItem().Envelope;
        envelope.Headers["traceparent"].ShouldContain(activity!.TraceId.ToString());
    }

    [Fact]
    public void Payloads_use_camel_case_and_string_enums_and_omit_nulls()
    {
        using OutboxFixture fixture = new();
        Outbox outbox = fixture.Outbox;
        var orderId = Guid.Parse("0192f0c4-5b6e-7a3c-9d2e-1f4a5b6c7d8e");

        outbox.Enqueue(new OrderPlaced(orderId, OrderChannel.PhoneOrder));

        outbox.DequeuePending().ShouldHaveSingleItem().Envelope.Payload
            .ShouldBe("""{"orderId":"0192f0c4-5b6e-7a3c-9d2e-1f4a5b6c7d8e","channel":"phoneOrder"}""");
    }

    [Fact]
    public void Dequeue_clears_the_pending_messages()
    {
        using OutboxFixture fixture = new();
        Outbox outbox = fixture.Outbox;
        outbox.Enqueue(new OrderPlaced(Guid.NewGuid(), OrderChannel.WebShop));

        outbox.DequeuePending().Count.ShouldBe(1);
        outbox.DequeuePending().ShouldBeEmpty();
    }

    [Fact]
    public void Enqueuing_an_unregistered_type_fails_with_its_name()
    {
        using OutboxFixture fixture = new();
        Outbox outbox = fixture.Outbox;

        Should.Throw<InvalidOperationException>(() => outbox.Enqueue(new Uri("https://example.test")))
            .Message.ShouldContain(nameof(Uri));
    }
}
