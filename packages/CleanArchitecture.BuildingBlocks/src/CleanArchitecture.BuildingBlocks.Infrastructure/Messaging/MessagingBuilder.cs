using CleanArchitecture.BuildingBlocks.Integration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;

namespace CleanArchitecture.BuildingBlocks.Infrastructure.Messaging;

/// <summary>Declares the message types a service produces and consumes.</summary>
public sealed class MessagingBuilder
{
    internal MessagingBuilder(IServiceCollection services, MessageTypeRegistry registry)
    {
        Services = services;
        Registry = registry;
    }

    public IServiceCollection Services { get; }

    internal MessageTypeRegistry Registry { get; }

    public MessagingBuilder Produces<TMessage>(string messageType, int schemaVersion, string stream)
        where TMessage : class
    {
        Registry.Register(typeof(TMessage), messageType, schemaVersion, stream);
        return this;
    }

    public MessagingBuilder Consumes<TMessage, THandler>(string messageType, int schemaVersion, string stream)
        where TMessage : class
        where THandler : class, IIntegrationMessageHandler<TMessage>
    {
        MessageRegistration registration = Registry.Register(typeof(TMessage), messageType, schemaVersion, stream);
        Registry.AddConsumer(registration, static (services, message, envelope, cancellationToken) =>
            services.GetRequiredService<THandler>().HandleAsync((TMessage)message, envelope, cancellationToken));
        Services.TryAddScoped<THandler>();
        return this;
    }
}
