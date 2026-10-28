using System.Diagnostics.CodeAnalysis;

namespace CleanArchitecture.BuildingBlocks.Infrastructure.Messaging;

public sealed record MessageRegistration(Type ClrType, string MessageType, int SchemaVersion, string Stream);

internal delegate Task<Result> HandlerInvoker(
    IServiceProvider services,
    object message,
    MessageEnvelope envelope,
    CancellationToken cancellationToken);

internal sealed record ConsumerRegistration(MessageRegistration Message, HandlerInvoker Invoke);

/// <summary>
/// Maps message CLR types to their stable names, schema versions and streams. Every service
/// registers what it produces and consumes explicitly at startup.
/// </summary>
public sealed class MessageTypeRegistry
{
    private readonly Dictionary<Type, MessageRegistration> _byClrType = [];
    private readonly Dictionary<(string MessageType, int SchemaVersion), MessageRegistration> _byName = [];
    private readonly Dictionary<(string MessageType, int SchemaVersion), ConsumerRegistration> _consumers = [];

    internal IEnumerable<ConsumerRegistration> Consumers => _consumers.Values;

    public MessageRegistration Get(Type clrType) =>
        _byClrType.TryGetValue(clrType, out MessageRegistration? registration)
            ? registration
            : throw new InvalidOperationException($"Message type '{clrType.FullName}' is not registered for messaging.");

    public bool TryGet(string messageType, int schemaVersion, [NotNullWhen(true)] out MessageRegistration? registration) =>
        _byName.TryGetValue((messageType, schemaVersion), out registration);

    internal bool TryGetConsumer(MessageEnvelope envelope, [NotNullWhen(true)] out ConsumerRegistration? consumer) =>
        _consumers.TryGetValue((envelope.MessageType, envelope.SchemaVersion), out consumer);

    internal MessageRegistration Register(Type clrType, string messageType, int schemaVersion, string stream)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(messageType);
        ArgumentException.ThrowIfNullOrWhiteSpace(stream);
        ArgumentOutOfRangeException.ThrowIfLessThan(schemaVersion, 1);

        var registration = new MessageRegistration(clrType, messageType, schemaVersion, stream);

        if (_byClrType.TryGetValue(clrType, out MessageRegistration? existing))
        {
            return existing == registration
                ? existing
                : throw new InvalidOperationException($"Message type '{clrType.FullName}' is already registered as '{existing.MessageType}' v{existing.SchemaVersion} on '{existing.Stream}'.");
        }

        if (!_byName.TryAdd((messageType, schemaVersion), registration))
        {
            throw new InvalidOperationException($"'{messageType}' v{schemaVersion} is already registered for another CLR type.");
        }

        _byClrType.Add(clrType, registration);
        return registration;
    }

    internal void AddConsumer(MessageRegistration message, HandlerInvoker invoke)
    {
        if (!_consumers.TryAdd((message.MessageType, message.SchemaVersion), new ConsumerRegistration(message, invoke)))
        {
            throw new InvalidOperationException($"'{message.MessageType}' v{message.SchemaVersion} already has a handler.");
        }
    }
}
