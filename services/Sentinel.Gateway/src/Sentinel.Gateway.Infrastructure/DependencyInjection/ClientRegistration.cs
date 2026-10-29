using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sentinel.Gateway.Application.Abstractions.Providers;
using Sentinel.Gateway.Application.ControlPlane;
using Sentinel.Gateway.Infrastructure.Clients;

namespace Sentinel.Gateway.Infrastructure.DependencyInjection;

internal static class ClientRegistration
{
    /// <summary>
    /// The peers the Gateway reads from and relays to: Runtime's incidents and history, AgentWorker's
    /// executions, Executor's policy and controls, Ingestion's sources, and every configuration area's
    /// owner (Phase 08). The Gateway authorizes and audits; the owners validate.
    /// </summary>
    public static IServiceCollection AddGatewayClients(this IServiceCollection services, IConfiguration configuration)
    {
        Uri runtime = Required(configuration, "Gateway:Services:Runtime");
        Uri agentWorker = Required(configuration, "Gateway:Services:AgentWorker");
        Uri executor = Required(configuration, "Gateway:Services:Executor");
        Uri ingestion = Required(configuration, "Gateway:Services:Ingestion");
        services.AddHttpClient<IRuntimeIncidents, RuntimeIncidentsClient>(client => client.BaseAddress = runtime);
        services.AddHttpClient<IRuntimeHistory, RuntimeHistoryClient>(client => client.BaseAddress = runtime);
        services.AddHttpClient<IRuntimeRemediation, RuntimeRemediationClient>(client => client.BaseAddress = runtime);
        services.AddHttpClient<IAgentExecutions, AgentExecutionsClient>(client => client.BaseAddress = agentWorker);
        services.AddHttpClient<IExecutorControlPlane, ExecutorControlPlaneClient>(client => client.BaseAddress = executor);
        services.AddHttpClient<ISourceHealth, SourceHealthClient>(client => client.BaseAddress = ingestion);

        // Runtime, AgentWorker and Ingestion own their areas; the Gateway owns tenants and notifications.
        services.AddHttpClient(ConfigurationAreas.Runtime, client => client.BaseAddress = runtime);
        services.AddHttpClient(ConfigurationAreas.Models, client => client.BaseAddress = agentWorker);
        services.AddHttpClient(ConfigurationAreas.Sources, client => client.BaseAddress = ingestion);
        services.AddSingleton<IConfigurationOwners, ConfigurationOwnersClient>();
        return services;
    }

    private static Uri Required(IConfiguration configuration, string key) =>
        new(configuration[key] ?? throw new InvalidOperationException($"{key} is not configured."));
}
