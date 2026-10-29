using CleanArchitecture.BuildingBlocks.Infrastructure.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Sentinel.Gateway.Application.Abstractions.Persistence;
using Sentinel.Gateway.Infrastructure.Persistence;

namespace Sentinel.Gateway.Infrastructure.DependencyInjection;

internal static class PersistenceRegistration
{
    /// <summary>The Gateway's stores and its schema with the messaging tables.</summary>
    public static IServiceCollection AddGatewayPersistence(this IServiceCollection services)
    {
        services.AddSingleton<IMessagingStoreInitializer, ApiSchema>();
        services.AddScoped<INarrativeStore, PostgresNarrativeStore>();
        services.AddScoped<IAuditLog, PostgresAuditLog>();
        services.AddScoped<ILiveEventStore, PostgresLiveEventStore>();
        services.AddScoped<INotificationStore, PostgresNotificationStore>();
        services.AddSingleton<IIdempotencyStore, PostgresIdempotencyStore>();
        services.AddSingleton<IAnalyticsStore, PostgresAnalyticsStore>();
        return services;
    }
}
