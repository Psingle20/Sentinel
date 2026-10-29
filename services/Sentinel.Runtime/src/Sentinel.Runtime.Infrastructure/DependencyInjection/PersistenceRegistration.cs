using CleanArchitecture.BuildingBlocks.Infrastructure.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Sentinel.Runtime.Application.Abstractions.Persistence;
using Sentinel.Runtime.Infrastructure.Persistence;

namespace Sentinel.Runtime.Infrastructure.DependencyInjection;

internal static class PersistenceRegistration
{
    /// <summary>Runtime's own database (ADR-009): the incident event store and its read model, correlation state and incident memory.</summary>
    public static IServiceCollection AddRuntimePersistence(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<IMessagingStoreInitializer, RuntimeSchema>();
        services.AddScoped<IIncidentStore, PostgresIncidentStore>();
        services.AddScoped<ICorrelationStore, PostgresCorrelationStore>();
        services.AddScoped<IIncidentMemoryStore, PostgresIncidentMemoryStore>();
        return services;
    }
}
