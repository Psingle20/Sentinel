using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sentinel.Runtime.Application.Abstractions.Runtime;
using Sentinel.Runtime.Infrastructure.Workflows;

namespace Sentinel.Runtime.Infrastructure.DependencyInjection;

internal static class WorkflowRegistration
{
    /// <summary>The workflow engine (ADR-005): the client, the worker and its activities, and the reconciler that repairs missing workflows.</summary>
    public static IServiceCollection AddRuntimeWorkflows(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<TemporalOptions>()
            .Bind(configuration.GetSection(TemporalOptions.Section))
            .Validate(options => !string.IsNullOrWhiteSpace(options.Address), "Runtime:Temporal:Address is not configured.")
            .ValidateOnStart();
        services.AddSingleton<TemporalClientProvider>();
        services.AddSingleton<IncidentActivities>();
        services.AddSingleton<IIncidentWorkflows, TemporalIncidentWorkflows>();
        services.AddSingleton<TemporalWorkerHost>();
        services.AddHostedService(provider => provider.GetRequiredService<TemporalWorkerHost>());
        services.AddHostedService<WorkflowReconciler>();
        return services;
    }

    /// <summary>Runtime is ready when its workflow worker polls Temporal.</summary>
    public static IHealthChecksBuilder AddRuntimeWorkflowHealth(this IHealthChecksBuilder checks, params string[] tags) =>
        checks.AddCheck<TemporalHealthCheck>("temporal", tags: tags);
}
