using System.Net.Http.Headers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sentinel.Ingestion.Application.Abstractions.Providers;
using Sentinel.Ingestion.Infrastructure.Providers;

namespace Sentinel.Ingestion.Infrastructure.DependencyInjection;

internal static class ProviderRegistration
{
    /// <summary>Largest provider response read into memory; a bigger one fails the pull instead of growing the process (Phase 02).</summary>
    public const long MaxProviderResponseBytes = 16 * 1024 * 1024;

    /// <summary>
    /// The provider adapters. Provider endpoints and credentials come from <c>Ingestion:Providers</c>,
    /// supplied by local secret configuration or the orchestrator, never from source (ADR-007: only
    /// Ingestion holds them).
    /// </summary>
    public static IServiceCollection AddIngestionProviders(this IServiceCollection services, IConfiguration configuration)
    {
        Uri prometheus = RequiredUri(configuration, "Ingestion:Providers:Prometheus:Endpoint");
        services.AddHttpClient<IMetricsProvider, PrometheusMetricsProvider>(client => Bounded(client, prometheus));

        string deployToken = Required(configuration, "Ingestion:Providers:DeployController:Token");
        Uri deployController = new(configuration["Ingestion:Providers:DeployController:Endpoint"] ?? "http://deploy-controller");
        services.AddHttpClient<IDeploymentProvider, DeployControllerProvider>(client =>
        {
            Bounded(client, deployController);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", deployToken);
        });

        // Logs, traces and topology are optional: a deployment without the backend configured skips them.
        if (configuration["Ingestion:Providers:Loki:Endpoint"] is { Length: > 0 } loki)
        {
            services.AddHttpClient<ILogsProvider, LokiLogsProvider>(client => Bounded(client, new Uri(loki)));
        }

        if (configuration["Ingestion:Providers:Jaeger:Endpoint"] is { Length: > 0 } jaeger)
        {
            services.AddHttpClient<JaegerProvider>(client => Bounded(client, new Uri(jaeger)));
            services.AddTransient<ITracesProvider>(provider => provider.GetRequiredService<JaegerProvider>());
            services.AddTransient<ITopologyProvider>(provider => provider.GetRequiredService<JaegerProvider>());
        }

        services.AddSingleton<IKnowledgeProvider, RunbookDirectoryProvider>();
        return services;
    }

    private static void Bounded(HttpClient client, Uri endpoint)
    {
        client.BaseAddress = endpoint;
        client.MaxResponseContentBufferSize = MaxProviderResponseBytes;
        client.Timeout = TimeSpan.FromSeconds(30);
    }

    private static string Required(IConfiguration configuration, string key) =>
        configuration[key] is { Length: > 0 } value
            ? value
            : throw new InvalidOperationException($"{key} is not configured. Set it through local secret configuration or the orchestrator.");

    private static Uri RequiredUri(IConfiguration configuration, string key) => new(Required(configuration, key));
}
