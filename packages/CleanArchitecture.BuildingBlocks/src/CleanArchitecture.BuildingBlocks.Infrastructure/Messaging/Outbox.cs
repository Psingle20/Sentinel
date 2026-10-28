using CleanArchitecture.BuildingBlocks.Integration;
using Microsoft.Extensions.Options;

namespace CleanArchitecture.BuildingBlocks.Infrastructure.Messaging;

/// <summary>
/// Scoped outbox: builds envelopes for enqueued messages and holds them until the scope's
/// unit of work commits them together with the domain changes.
/// </summary>
internal sealed class Outbox(
    MessageTypeRegistry registry,
    IOptions<MessagingOptions> options,
    IMessageContextAccessor context,
    TimeProvider timeProvider) : IOutbox, IPendingOutbox
{
    private readonly List<OutboxRecord> _pending = [];

    public void Enqueue<TMessage>(TMessage message, MessageMetadata? metadata = null)
        where TMessage : class
    {
        ArgumentNullException.ThrowIfNull(message);

        MessageRegistration registration = registry.Get(typeof(TMessage));
        MessageEnvelope? cause = context.Current;
        var messageId = Guid.CreateVersion7(timeProvider.GetUtcNow());

        // Inbound headers flow on by default (for example tenant identity); trace context is
        // replaced with the current span so the trace continues from this handler.
        Dictionary<string, string> headers = cause?.Headers
            .Where(header => !MessagingDiagnostics.IsTraceHeader(header.Key))
            .ToDictionary(header => header.Key, header => header.Value) ?? [];
        foreach (KeyValuePair<string, string> header in metadata?.Headers ?? new Dictionary<string, string>())
        {
            headers[header.Key] = header.Value;
        }
        MessagingDiagnostics.InjectTraceContext(headers);

        var envelope = new MessageEnvelope(
            messageId,
            registration.MessageType,
            registration.SchemaVersion,
            timeProvider.GetUtcNow(),
            options.Value.ServiceName,
            metadata?.CorrelationId ?? cause?.CorrelationId ?? messageId.ToString(),
            metadata?.CausationId ?? cause?.MessageId.ToString(),
            headers,
            MessageSerializer.Serialize(message, typeof(TMessage)));

        _pending.Add(new OutboxRecord(envelope, registration.Stream));
    }

    public IReadOnlyList<OutboxRecord> DequeuePending()
    {
        OutboxRecord[] pending = [.. _pending];
        _pending.Clear();
        return pending;
    }
}
