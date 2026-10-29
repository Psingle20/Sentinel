using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sentinel.Runtime.Application.Abstractions.Providers;
using Sentinel.Runtime.Application.Abstractions.Security;
using Sentinel.Runtime.Infrastructure.Clients;

namespace Sentinel.Runtime.Infrastructure.DependencyInjection;

internal static class ClientRegistration
{
    /// <summary>
    /// Executor's plan-time policy evaluation, Ingestion's observation queries and the gateway's published approval keys,
    /// by service-discovery name (ADR-010).
    /// </summary>
    public static IServiceCollection AddRuntimeClients(this IServiceCollection services, IConfiguration configuration)
    {
        Uri executor = RequiredUri(configuration, "Runtime:Services:Executor");
        Uri ingestion = RequiredUri(configuration, "Runtime:Services:Ingestion");
        Uri gateway = RequiredUri(configuration, "Runtime:Services:Gateway");
        services.AddHttpClient<IPolicyEvaluator, ExecutorPolicyClient>(client => client.BaseAddress = executor);
        services.AddHttpClient<IObservationQueries, IngestionQueryClient>(client => client.BaseAddress = ingestion);

        // A singleton, so verified keys stay cached across requests.
        services.AddHttpClient(GatewayApprovalKeys.ClientName, client => client.BaseAddress = gateway);
        services.AddSingleton<IApprovalKeyResolver, GatewayApprovalKeys>();
        return services;
    }

    private static Uri RequiredUri(IConfiguration configuration, string key) =>
        new(configuration[key] ?? throw new InvalidOperationException($"{key} is not configured."));
}
