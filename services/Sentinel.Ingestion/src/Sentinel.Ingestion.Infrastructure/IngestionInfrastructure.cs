using System.Net.Http.Headers;
using CleanArchitecture.BuildingBlocks.Infrastructure.Messaging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Sentinel.Ingestion.Application;
using Sentinel.Ingestion.Application.Knowledge;
using Sentinel.Ingestion.Application.Providers;
using Sentinel.Ingestion.Infrastructure.Providers;
using Sentinel.Ingestion.Infrastructure.Storage;

namespace Sentinel.Ingestion.Infrastructure;

public static class IngestionInfrastructure
{
    /// <summary>
    /// Provider adapters, the observation store and the ingestion worker. Provider endpoints and
    /// credentials come from <c>Ingestion:Providers</c>, supplied by local secret configuration or
    /// the orchestrator, never from source (ADR-007: only Ingestion holds them).
    /// </summary>
    public static IServiceCollection AddIngestionInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<IngestionOptions>()
            .Bind(configuration.GetSection(IngestionOptions.Section))
            .Validate(options => options.IsValid(), "Ingestion options need a tenant, environment, positive intervals and complete metric series.")
            .ValidateOnStart();
        services.TryAddSingleton(TimeProvider.System);

        services.AddSingleton<IMessagingStoreInitializer, IngestionStoreInitializer>();
        services.AddScoped<IObservationStore, PostgresObservationStore>();
        services.AddScoped<ISourceStateStore, PostgresSourceStateStore>();
        services.AddScoped<IngestionCycle>();
        services.AddScoped<ObservationQueryService>();
        services.AddSingleton<QueryBulkhead>();

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
        services.AddScoped<KnowledgeSearch>();

        services.AddSingleton<IngestionTelemetry>();
        services.AddSingleton<IIngestionMetrics>(provider => provider.GetRequiredService<IngestionTelemetry>());
        services.AddHostedService<IngestionWorker>();
        return services;
    }

    /// <summary>Largest provider response read into memory; a bigger one fails the pull instead of growing the process (Phase 02).</summary>
    public const long MaxProviderResponseBytes = 16 * 1024 * 1024;

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
