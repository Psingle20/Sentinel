using CleanArchitecture.BuildingBlocks.Infrastructure.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Sentinel.AgentWorker.Application.Abstractions.Persistence;
using Sentinel.AgentWorker.Infrastructure.Persistence;

namespace Sentinel.AgentWorker.Infrastructure.DependencyInjection;

internal static class PersistenceRegistration
{
    /// <summary>The execution store in AgentWorker's database, and its schema with the messaging tables.</summary>
    public static IServiceCollection AddAgentWorkerPersistence(this IServiceCollection services)
    {
        services.AddSingleton<IMessagingStoreInitializer, AgentWorkerSchema>();
        services.AddScoped<IExecutionStore, PostgresExecutionStore>();
        return services;
    }
}
