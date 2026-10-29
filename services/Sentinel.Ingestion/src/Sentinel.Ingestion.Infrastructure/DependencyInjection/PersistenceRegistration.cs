using CleanArchitecture.BuildingBlocks.Infrastructure.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Sentinel.Ingestion.Application.Abstractions.Persistence;
using Sentinel.Ingestion.Infrastructure.Persistence;

namespace Sentinel.Ingestion.Infrastructure.DependencyInjection;

internal static class PersistenceRegistration
{
    /// <summary>The observation and source-state stores in Ingestion's database, and its schema with the messaging tables.</summary>
    public static IServiceCollection AddIngestionPersistence(this IServiceCollection services)
    {
        services.AddSingleton<IMessagingStoreInitializer, IngestionStoreInitializer>();
        services.AddScoped<IObservationStore, PostgresObservationStore>();
        services.AddScoped<ISourceStateStore, PostgresSourceStateStore>();
        return services;
    }
}
