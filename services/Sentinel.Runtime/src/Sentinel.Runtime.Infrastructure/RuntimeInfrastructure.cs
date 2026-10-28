using System.Net;
using CleanArchitecture.BuildingBlocks.Infrastructure.Messaging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Orleans.Configuration;
using Orleans.Serialization;
using Sentinel.Contracts.Messaging;
using Sentinel.Runtime.Application;
using Sentinel.Runtime.Application.Incidents;
using Sentinel.Runtime.Application.Memory;
using Sentinel.Runtime.Infrastructure.Grains;
using Sentinel.Runtime.Infrastructure.Storage;
using Sentinel.Runtime.Infrastructure.Workflows;
using Temporalio.Extensions.OpenTelemetry;

namespace Sentinel.Runtime.Infrastructure;

public static class RuntimeInfrastructure
{
    /// <summary>The activity sources Runtime's actor runtime and workflow engine trace with.</summary>
    public static readonly IReadOnlyList<string> TraceSources =
    [
        TracingInterceptor.ClientSource.Name,
        TracingInterceptor.WorkflowsSource.Name,
        TracingInterceptor.ActivitiesSource.Name,
        "Microsoft.Orleans.Runtime",
        "Microsoft.Orleans.Application"
    ];

    public static IServiceCollection AddRuntimeInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<RuntimeOptions>()
            .Bind(configuration.GetSection(RuntimeOptions.Section))
            .Validate(options => options.IsValid(), "Runtime needs a detection rule with a detection threshold above its onset threshold, known investigation roles and positive workflow timeouts.")
            .ValidateOnStart();
        services.AddOptions<TemporalOptions>()
            .Bind(configuration.GetSection(TemporalOptions.Section))
            .Validate(options => !string.IsNullOrWhiteSpace(options.Address), "Runtime:Temporal:Address is not configured.")
            .ValidateOnStart();
        services.TryAddSingleton(TimeProvider.System);

        services.AddSingleton<IMessagingStoreInitializer, RuntimeSchema>();
        services.AddScoped<IIncidentStore, PostgresIncidentStore>();
        services.AddScoped<ICorrelationStore, PostgresCorrelationStore>();
        services.AddScoped<IncidentCommandExecutor>();
        services.AddScoped<IncidentSteps>();
        services.AddScoped<RecoveryAssessor>();
        services.AddScoped<DetectionService>();
        services.AddScoped<RemediationControlService>();
        services.AddScoped<IncidentQueryService>();
        services.AddScoped<ProjectionRebuilder>();
        services.AddScoped<IIncidentMemoryStore, PostgresIncidentMemoryStore>();
        services.AddScoped<IncidentMemoryRecorder>();
        services.AddScoped<IncidentMemoryService>();
        services.AddScoped<DomainReplayService>();
        services.AddScoped<PostmortemService>();
        services.AddSingleton<ServiceTopologyCache>();

        // The single writers are grains (ADR-004).
        services.AddSingleton<IIncidentOwner, GrainIncidentOwner>();
        services.AddSingleton<ICorrelationPartitions, GrainCorrelationPartitions>();
        services.AddSingleton<IServiceCatalog, GrainServiceCatalog>();

        // The workflow engine (ADR-005).
        services.AddSingleton<TemporalClientProvider>();
        services.AddSingleton<IncidentActivities>();
        services.AddSingleton<IIncidentWorkflows, TemporalIncidentWorkflows>();
        services.AddSingleton<TemporalWorkerHost>();
        services.AddHostedService(provider => provider.GetRequiredService<TemporalWorkerHost>());
        services.AddHostedService<WorkflowReconciler>();

        // Service-discovery names, resolved by the orchestrator (ADR-010).
        Uri executor = RequiredUri(configuration, "Runtime:Services:Executor");
        Uri ingestion = RequiredUri(configuration, "Runtime:Services:Ingestion");
        services.AddHttpClient<IPolicyEvaluator, ExecutorPolicyClient>(client => client.BaseAddress = executor);
        services.AddHttpClient<IObservationQueries, IngestionQueryClient>(client => client.BaseAddress = ingestion);
        return services;
    }

    /// <summary>Runtime is ready when its workflow worker polls Temporal.</summary>
    public static IHealthChecksBuilder AddRuntimeHealthChecks(this IHealthChecksBuilder checks, params string[] tags) =>
        checks.AddCheck<TemporalHealthCheck>("temporal", tags: tags);

    /// <summary>
    /// Hosts Runtime's grains (ADR-004). Membership is kept in the messaging Redis, so Runtime
    /// instances find each other without another component; <c>Runtime:Orleans:Clustering=localhost</c>
    /// runs a single development silo without it. Grain-call payloads use the contract JSON rules.
    /// </summary>
    public static IHostApplicationBuilder AddRuntimeActors(this IHostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        IConfiguration configuration = builder.Configuration;
        builder.UseOrleans(silo =>
        {
            if (configuration["Runtime:Orleans:Clustering"] == "localhost")
            {
                silo.UseLocalhostClustering();
            }
            else
            {
                silo.UseRedisClustering(configuration.GetConnectionString("messaging")
                    ?? throw new InvalidOperationException("The messaging connection string is not configured."));
                silo.ConfigureEndpoints(
                    IPAddress.Loopback,
                    configuration.GetValue("Runtime:Orleans:SiloPort", 11111),
                    configuration.GetValue("Runtime:Orleans:GatewayPort", 30000));
            }

            silo.Configure<ClusterOptions>(options =>
            {
                options.ClusterId = configuration["Runtime:Orleans:ClusterId"] ?? "sentinel-runtime";
                options.ServiceId = "sentinel-runtime";
            });
            silo.Configure<SiloMessagingOptions>(options => options.ResponseTimeout = TimeSpan.FromMinutes(2));
            silo.Services.AddRuntimeGrainSerialization();
        });
        return builder;
    }

    /// <summary>Grain-call payloads are Sentinel types, serialized with the contract JSON rules (ADR-012).</summary>
    public static IServiceCollection AddRuntimeGrainSerialization(this IServiceCollection services) =>
        services.AddSerializer(serializer => serializer.AddJsonSerializer(
            isSupported: type => type.Namespace?.StartsWith("Sentinel.", StringComparison.Ordinal) == true,
            jsonSerializerOptions: SentinelJson.Options));

    private static Uri RequiredUri(IConfiguration configuration, string key) =>
        new(configuration[key] ?? throw new InvalidOperationException($"{key} is not configured."));
}
