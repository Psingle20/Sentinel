using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Infrastructure.Messaging;
using CleanArchitecture.BuildingBlocks.Infrastructure.PostgreSql;
using CleanArchitecture.BuildingBlocks.Infrastructure.Redis;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Sentinel.Contracts.Messaging;

namespace Sentinel.ServiceDefaults;

/// <summary>
/// The infrastructure every Sentinel service shares: its own PostgreSQL database (ADR-009), the
/// messaging Redis (ADR-008) and building-block messaging with the outbox and inbox in that
/// database. Connection strings come from Aspire as <c>{service}-db</c> and <c>messaging</c>.
/// </summary>
public static class SentinelMessagingExtensions
{
    public const string MessagingConnectionName = "messaging";

    /// <summary>The identity headers of a Sentinel message and the tag each takes in traces and log scopes.</summary>
    public static readonly IReadOnlyDictionary<string, string> ContextTags = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [SentinelHeaders.TenantId] = SentinelTags.TenantId,
        [SentinelHeaders.EnvironmentId] = SentinelTags.EnvironmentId,
        [SentinelHeaders.ServiceId] = SentinelTags.ServiceId,
        [SentinelHeaders.IncidentId] = SentinelTags.IncidentId
    };

    public static MessagingBuilder AddSentinelMessaging(
        this IHostApplicationBuilder builder,
        string serviceName,
        Action<OutboxOptions>? configureOutbox = null,
        Action<RedisStreamsOptions>? configureStreams = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceName);

        string database = builder.Configuration.GetConnectionString(DatabaseConnectionName(serviceName))
            ?? throw new InvalidOperationException($"Connection string '{DatabaseConnectionName(serviceName)}' is not configured.");
        string redis = builder.Configuration.GetConnectionString(MessagingConnectionName)
            ?? throw new InvalidOperationException($"Connection string '{MessagingConnectionName}' is not configured.");

        builder.Services.AddPostgresDataSource(database, ServiceDefaultsExtensions.ReadyTag);
        builder.Services.AddRedisConnection(redis, ServiceDefaultsExtensions.ReadyTag);

        // Whose message it is goes on every consumer span and into the log scope of every handler, so traces and logs
        // can be filtered by tenant, environment, service and incident.
        builder.Services.Configure<MessagingOptions>(options =>
        {
            foreach ((string header, string tag) in ContextTags)
            {
                options.ContextHeaders[header] = tag;
            }
        });
        return builder.Services
            .AddMessaging(ProducerName(serviceName), configureOutbox)
            .AddPostgresStorage()
            .AddRedisStreams(configureStreams);
    }

    public static string DatabaseConnectionName(string serviceName) => $"{serviceName}-db";

    public static string ProducerName(string serviceName) => $"sentinel-{serviceName}";

    /// <summary>Registers a Sentinel message this service publishes, with its contract's name, version and stream.</summary>
    public static MessagingBuilder Produces<TMessage>(this MessagingBuilder messaging)
        where TMessage : class, ISentinelMessage
    {
        ArgumentNullException.ThrowIfNull(messaging);

        return messaging.Produces<TMessage>(TMessage.MessageType, TMessage.SchemaVersion, TMessage.Stream);
    }

    /// <summary>
    /// Registers a handler for a Sentinel message. Deliveries whose identity headers do not match
    /// the payload's tenant and environment are refused (ADR-012).
    /// </summary>
    public static MessagingBuilder Consumes<TMessage, THandler>(this MessagingBuilder messaging)
        where TMessage : class, ISentinelMessage
        where THandler : class, IIntegrationMessageHandler<TMessage>
    {
        ArgumentNullException.ThrowIfNull(messaging);

        messaging.Services.TryAddScoped<THandler>();
        return messaging.Consumes<TMessage, IdentityCheckedHandler<TMessage, THandler>>(TMessage.MessageType, TMessage.SchemaVersion, TMessage.Stream);
    }

    internal sealed class IdentityCheckedHandler<TMessage, THandler>(THandler inner) : IIntegrationMessageHandler<TMessage>
        where TMessage : class, ISentinelMessage
        where THandler : class, IIntegrationMessageHandler<TMessage>
    {
        public Task<Result> HandleAsync(TMessage message, MessageEnvelope envelope, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(envelope);

            return SentinelHeaders.MatchIdentity(envelope.Headers, message)
                ? inner.HandleAsync(message, envelope, cancellationToken)
                : Task.FromResult(Result.Failure(Error.Problem(
                    "Messaging.IdentityMismatch",
                    $"{envelope.MessageType} {envelope.MessageId} carries identity headers that do not match its payload.")));
        }
    }
}
