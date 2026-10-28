using System.Diagnostics;
using System.Text.Json;
using CleanArchitecture.BuildingBlocks.Integration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CleanArchitecture.BuildingBlocks.Infrastructure.Messaging;

public enum ProcessingOutcome
{
    /// <summary>Handled and committed. Acknowledge the delivery.</summary>
    Processed,

    /// <summary>Already handled by this consumer. Acknowledge without handling again.</summary>
    Duplicate,

    /// <summary>Handling failed and was rolled back. Leave the delivery for a retry.</summary>
    Failed,

    /// <summary>Unsupported type or version, or a malformed payload. Dead-letter it; retrying cannot help.</summary>
    Rejected
}

public sealed record ProcessingResult(ProcessingOutcome Outcome, string? Reason = null);

/// <summary>Handles one delivered envelope. Transports call this and act on the outcome.</summary>
public interface IInboundMessageProcessor
{
    Task<ProcessingResult> ProcessAsync(MessageEnvelope envelope, CancellationToken cancellationToken);
}

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
}
