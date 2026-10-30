using System.Net.Http.Headers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sentinel.Executor.Application.Abstractions.Remediation;
using Sentinel.Executor.Infrastructure.Remediation;

namespace Sentinel.Executor.Infrastructure.DependencyInjection;

internal static class AdapterRegistration
{
    /// <summary>
    /// The privileged adapters (ADR-006). The deploy token comes from
    /// <c>Executor:Adapters:DeployController:Token</c>, supplied by secret configuration or the
    /// orchestrator: Executor is the only holder of privileged credentials. The adapter decides about
    /// unknown outcomes itself, so the clients do not retry.
    /// </summary>
    public static IServiceCollection AddRemediationAdapters(this IServiceCollection services, IConfiguration configuration)
    {
        Uri deployController = ConfigurationValues.RequiredUri(configuration, "Executor:Adapters:DeployController:Endpoint");
        string? deployToken = configuration["Executor:Adapters:DeployController:Token"];
        if (string.IsNullOrWhiteSpace(deployToken))
        {
            throw new InvalidOperationException("Executor:Adapters:DeployController:Token is not configured. Supply it through secret configuration or the orchestrator.");
        }

#pragma warning disable EXTEXP0001
        // Both adapters are typed clients of IRemediationAdapter, and a typed client takes its service type's name unless
        // given one; sharing a name, the batch adapter's address and headers replaced the rollback adapter's.
        services.AddHttpClient<IRemediationAdapter, DeployControllerRollbackAdapter>(nameof(DeployControllerRollbackAdapter), client =>
        {
            client.BaseAddress = deployController;
            client.Timeout = TimeSpan.FromSeconds(10);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", deployToken);
        }).RemoveAllResilienceHandlers();

        // The batch adapter is optional: without the worker's endpoint and control token, pause-batch has no adapter and is refused.
        if (configuration["Executor:Adapters:ReportingWorker:Endpoint"] is { Length: > 0 } reporting
            && configuration["Executor:Adapters:ReportingWorker:ControlToken"] is { } controlToken
            && !string.IsNullOrWhiteSpace(controlToken))
        {
            services.AddHttpClient<IRemediationAdapter, ReportingBatchAdapter>(nameof(ReportingBatchAdapter), client =>
            {
                client.BaseAddress = new Uri(reporting);
                client.Timeout = TimeSpan.FromSeconds(10);
                client.DefaultRequestHeaders.Add(ReportingBatchAdapter.ControlHeader, controlToken);
            }).RemoveAllResilienceHandlers();
        }
#pragma warning restore EXTEXP0001
        return services;
    }
}
