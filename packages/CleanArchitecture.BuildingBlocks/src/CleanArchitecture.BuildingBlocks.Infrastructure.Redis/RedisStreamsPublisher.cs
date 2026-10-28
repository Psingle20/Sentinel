using System.Diagnostics;
using CleanArchitecture.BuildingBlocks.Infrastructure.Messaging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace CleanArchitecture.BuildingBlocks.Infrastructure.Redis;

internal sealed class RedisStreamsPublisher(IConnectionMultiplexer redis, IOptions<RedisStreamsOptions> options) : IMessagePublisher
{
    public async Task PublishAsync(string stream, MessageEnvelope envelope, CancellationToken cancellationToken)
    {
        using Activity? activity = MessagingDiagnostics.ActivitySource.StartActivity($"publish {envelope.MessageType}", ActivityKind.Producer);
        activity?.SetTag("messaging.destination.name", stream);
        activity?.SetTag("messaging.message.id", envelope.MessageId.ToString());

        await redis.GetDatabase().StreamAddAsync(
            stream,
            RedisEnvelopeCodec.Encode(envelope),
            messageId: null,
            maxLength: options.Value.MaxStreamLength,
            useApproximateMaxLength: true);
    }
}
