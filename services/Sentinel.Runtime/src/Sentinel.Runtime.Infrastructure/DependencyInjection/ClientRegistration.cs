using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sentinel.Runtime.Application.Abstractions.Providers;
using Sentinel.Runtime.Infrastructure.Clients;

namespace Sentinel.Runtime.Infrastructure.DependencyInjection;

internal static class ClientRegistration
{
    /// <summary>Executor's plan-time policy evaluation and Ingestion's observation queries, by service-discovery name (ADR-010).</summary>
    public static IServiceCollection AddRuntimeClients(this IServiceCollection services, IConfiguration configuration)
    {
        Uri executor = RequiredUri(configuration, "Runtime:Services:Executor");
        Uri ingestion = RequiredUri(configuration, "Runtime:Services:Ingestion");
        services.AddHttpClient<IPolicyEvaluator, ExecutorPolicyClient>(client => client.BaseAddress = executor);
        services.AddHttpClient<IObservationQueries, IngestionQueryClient>(client => client.BaseAddress = ingestion);
        return services;
    }

    private static Uri RequiredUri(IConfiguration configuration, string key) =>
        new(configuration[key] ?? throw new InvalidOperationException($"{key} is not configured."));
}
