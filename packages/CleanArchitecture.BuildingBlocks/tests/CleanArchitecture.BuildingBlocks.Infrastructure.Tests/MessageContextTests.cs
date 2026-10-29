using System.Diagnostics;
using CleanArchitecture.BuildingBlocks.Infrastructure.Messaging;
using CleanArchitecture.BuildingBlocks.Integration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace CleanArchitecture.BuildingBlocks.Infrastructure.Tests;

/// <summary>Headers mapped in <see cref="MessagingOptions.ContextHeaders"/> reach the consumer's span and its handler's logs.</summary>
public sealed class MessageContextTests
{
    [Fact]
    public async Task Mapped_headers_tag_the_consumer_span_and_scope_the_handlers_logs()
    {
        var logger = new ScopeRecordingLogger();
        var stopped = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == MessagingDiagnostics.ActivitySourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity =>
            {
                lock (stopped)
                {
                    stopped.Add(activity);
                }
            }
        };
        ActivitySource.AddActivityListener(listener);
        await using ServiceProvider provider = Provider(logger, options =>
        {
            options.ContextHeaders["x-tenant"] = "tenant.id";
            options.ContextHeaders["x-incident"] = "incident.id";
        });
        MessageEnvelope envelope = Envelope(new Dictionary<string, string> { ["x-tenant"] = "acme", ["x-unmapped"] = "ignored" });

        ProcessingResult result = await provider.GetRequiredService<IInboundMessageProcessor>().ProcessAsync(envelope, CancellationToken.None);

        result.Outcome.ShouldBe(ProcessingOutcome.Processed);
        logger.Scopes.ShouldHaveSingleItem().ShouldBe(new Dictionary<string, object?>(StringComparer.Ordinal) { ["tenant.id"] = "acme" });
        Activity consumer;
        lock (stopped)
        {
            consumer = stopped.Single(activity => activity.GetTagItem("messaging.message.id") as string == envelope.MessageId.ToString());
        }

        consumer.GetTagItem("tenant.id").ShouldBe("acme");
        consumer.GetTagItem("incident.id").ShouldBeNull();
        consumer.GetTagItem("x-unmapped").ShouldBeNull();
    }

    [Fact]
    public async Task Without_mapped_headers_no_scope_is_opened()
    {
        var logger = new ScopeRecordingLogger();
        await using ServiceProvider provider = Provider(logger, _ => { });

        ProcessingResult result = await provider.GetRequiredService<IInboundMessageProcessor>()
            .ProcessAsync(Envelope(new Dictionary<string, string> { ["x-tenant"] = "acme" }), CancellationToken.None);

        result.Outcome.ShouldBe(ProcessingOutcome.Processed);
        logger.Scopes.ShouldBeEmpty();
    }

    private static ServiceProvider Provider(ScopeRecordingLogger logger, Action<MessagingOptions> configure)
    {
        ServiceCollection services = new();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddSingleton<ILogger<InboundMessageProcessor>>(logger);
        var inbox = new InMemoryInbox();
        var unitOfWork = new CountingUnitOfWork();
        services.AddSingleton(inbox);
        services.AddSingleton<IInboxStore>(inbox);
        services.AddSingleton(unitOfWork);
        services.AddSingleton<IUnitOfWork>(unitOfWork);
        services.AddSingleton(new RecordingHandler());
        services.AddMessaging("billing").Consumes<OrderPlaced, RecordingHandler>("orders.order.placed", 1, "test.orders");
        services.Configure(configure);
        return services.BuildServiceProvider();
    }

    private static MessageEnvelope Envelope(IReadOnlyDictionary<string, string> headers) =>
        new(
            Guid.CreateVersion7(),
            "orders.order.placed",
            1,
            DateTimeOffset.UtcNow,
            "orders",
            "flow-1",
            null,
            headers,
            """{"orderId":"0192f0c4-5b6e-7a3c-9d2e-1f4a5b6c7d8e","channel":"webShop"}""");
}
