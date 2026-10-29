using System.Diagnostics;
using System.Text.Json;
using CleanArchitecture.BuildingBlocks.Integration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CleanArchitecture.BuildingBlocks.Infrastructure.Messaging;

/// <summary>
/// Records the message in the inbox, runs its handler and commits both in one unit of work,
/// so a message's effects happen exactly once even when it is delivered more than once.
/// </summary>
internal sealed class InboundMessageProcessor(
    IServiceScopeFactory scopeFactory,
    MessageTypeRegistry registry,
    IOptions<MessagingOptions> options,
    ILogger<InboundMessageProcessor> logger) : IInboundMessageProcessor
{
    public async Task<ProcessingResult> ProcessAsync(MessageEnvelope envelope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        if (!registry.TryGetConsumer(envelope, out ConsumerRegistration? consumer))
        {
            return new ProcessingResult(ProcessingOutcome.Rejected, $"No handler for '{envelope.MessageType}' v{envelope.SchemaVersion}.");
        }

        object message;
        try
        {
            message = MessageSerializer.Deserialize(envelope.Payload, consumer.Message.ClrType);
        }
        catch (JsonException exception)
        {
            return new ProcessingResult(ProcessingOutcome.Rejected, $"Malformed payload: {exception.Message}");
        }

        using Activity? activity = MessagingDiagnostics.StartConsumerActivity(envelope);
        Dictionary<string, object?> context = Context(envelope);
        foreach ((string name, object? value) in context)
        {
            activity?.SetTag(name, value);
        }

        // The handler and everything it logs run inside this scope, so its logs say whose message it was.
        using IDisposable? logScope = context.Count > 0 ? logger.BeginScope(context) : null;

        try
        {
            await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
            ((MessageContextAccessor)scope.ServiceProvider.GetRequiredService<IMessageContextAccessor>()).Current = envelope;

            IInboxStore inbox = scope.ServiceProvider.GetRequiredService<IInboxStore>();
            if (!await inbox.TryRecordAsync(options.Value.ServiceName, envelope, cancellationToken))
            {
                return new ProcessingResult(ProcessingOutcome.Duplicate);
            }

            Result result = await consumer.Invoke(scope.ServiceProvider, message, envelope, cancellationToken);
            if (result.IsFailure)
            {
                activity?.SetStatus(ActivityStatusCode.Error, result.Error.Code);
                return new ProcessingResult(ProcessingOutcome.Failed, $"{result.Error.Code}: {result.Error.Description}");
            }

            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().CommitAsync(cancellationToken);
            return new ProcessingResult(ProcessingOutcome.Processed);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            activity?.SetStatus(ActivityStatusCode.Error, exception.Message);
            logger.LogWarning(exception, "Handling {MessageType} {MessageId} failed", envelope.MessageType, envelope.MessageId);
            return new ProcessingResult(ProcessingOutcome.Failed, exception.Message);
        }
    }

    private Dictionary<string, object?> Context(MessageEnvelope envelope)
    {
        var context = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach ((string header, string name) in options.Value.ContextHeaders)
        {
            if (envelope.Headers.TryGetValue(header, out string? value))
            {
                context[name] = value;
            }
        }

        return context;
    }
}
