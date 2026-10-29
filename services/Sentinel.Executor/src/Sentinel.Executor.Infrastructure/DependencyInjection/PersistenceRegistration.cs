using CleanArchitecture.BuildingBlocks.Infrastructure.Messaging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Sentinel.Executor.Application.Abstractions.Persistence;
using Sentinel.Executor.Application.Options;
using Sentinel.Executor.Infrastructure.Persistence;

namespace Sentinel.Executor.Infrastructure.DependencyInjection;

internal static class PersistenceRegistration
{
    /// <summary>Executor's options and its own database (ADR-009): execution requests, operator controls and published policy versions over the repository's policy file.</summary>
    public static IServiceCollection AddExecutorPersistence(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<ExecutorOptions>().Bind(configuration.GetSection(ExecutorOptions.Section));
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<IMessagingStoreInitializer, ExecutorSchema>();
        services.AddScoped<IExecutionStore, PostgresExecutionStore>();
        services.AddScoped<IControlStore, PostgresControlStore>();
        services.AddSingleton<FilePolicyStore>();
        services.AddScoped<IPolicyStore, VersionedPolicyStore>();
        return services;
    }
}
