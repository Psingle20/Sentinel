using CleanArchitecture.BuildingBlocks.Integration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;

namespace CleanArchitecture.BuildingBlocks.Infrastructure.Messaging;

public static class MessagingServiceCollectionExtensions
{
    public const string OutboxHealthCheckName = "outbox-lag";

    /// <summary>
    /// Adds the outbox, inbox pipeline and dispatcher. A storage package (for example PostgreSQL)
    /// and a transport package (for example Redis Streams) complete the setup.
    /// </summary>
    public static MessagingBuilder AddMessaging(
        this IServiceCollection services,
        string serviceName,
        Action<OutboxOptions>? configureOutbox = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceName);

        if (services.Any(descriptor => descriptor.ServiceType == typeof(MessageTypeRegistry)))
        {
            throw new InvalidOperationException("AddMessaging has already been called for this service collection.");
        }

        var registry = new MessageTypeRegistry();
        services.AddSingleton(registry);
        services.TryAddSingleton(TimeProvider.System);

        services.AddOptions<MessagingOptions>().Configure(options => options.ServiceName = serviceName);
        services.AddOptions<OutboxOptions>()
            .Configure(options => configureOutbox?.Invoke(options))
            .Validate(options => options.IsValid(), "Outbox options must be positive, with MaxRetryDelay at least InitialRetryDelay.")
            .ValidateOnStart();

        services.AddScoped<MessageContextAccessor>();
        services.AddScoped<IMessageContextAccessor>(provider => provider.GetRequiredService<MessageContextAccessor>());
        services.AddScoped<Outbox>();
        services.AddScoped<IOutbox>(provider => provider.GetRequiredService<Outbox>());
        services.AddScoped<IPendingOutbox>(provider => provider.GetRequiredService<Outbox>());

        services.AddSingleton<IInboundMessageProcessor, InboundMessageProcessor>();
        services.AddSingleton<MessagingStartupGate>();
        services.AddHostedService<MessagingInitializer>();
        services.AddHostedService<OutboxDispatcher>();

        services.Configure<HealthCheckServiceOptions>(options => options.Registrations.Add(new HealthCheckRegistration(
            OutboxHealthCheckName,
            provider => ActivatorUtilities.CreateInstance<OutboxLagHealthCheck>(provider),
            HealthStatus.Degraded,
            ["ready"])));

        return new MessagingBuilder(services, registry);
    }
}
