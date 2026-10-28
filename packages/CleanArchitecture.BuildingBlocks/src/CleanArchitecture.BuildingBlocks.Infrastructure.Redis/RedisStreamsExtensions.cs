using CleanArchitecture.BuildingBlocks.Infrastructure.Messaging;
using Microsoft.Extensions.DependencyInjection;

namespace CleanArchitecture.BuildingBlocks.Infrastructure.Redis;

public static class RedisStreamsExtensions
{
    /// <summary>
    /// Publishes and consumes through Redis Streams. Requires an <c>IConnectionMultiplexer</c>
    /// for a Redis instance dedicated to messaging, with AOF persistence enabled.
    /// </summary>
    public static MessagingBuilder AddRedisStreams(this MessagingBuilder builder, Action<RedisStreamsOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddOptions<RedisStreamsOptions>()
            .Configure(options => configure?.Invoke(options))
            .Validate(options => options.IsValid(), "Redis Streams options must be positive and name a consumer.")
            .ValidateOnStart();

        builder.Services.AddSingleton<IMessagePublisher, RedisStreamsPublisher>();
        builder.Services.AddHostedService<RedisStreamsConsumer>();
        builder.Services.AddSingleton<RedisDeadLetters>();

        return builder;
    }
}
