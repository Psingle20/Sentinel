using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Trace;
using Temporalio.Extensions.OpenTelemetry;

namespace Sentinel.Runtime.Infrastructure.DependencyInjection;

internal static class RuntimeTracing
{
    /// <summary>The activity sources Runtime's actor runtime and workflow engine trace with.</summary>
    public static readonly IReadOnlyList<string> Sources =
    [
        TracingInterceptor.ClientSource.Name,
        TracingInterceptor.WorkflowsSource.Name,
        TracingInterceptor.ActivitiesSource.Name,
        "Microsoft.Orleans.Runtime",
        "Microsoft.Orleans.Application"
    ];

    public static IServiceCollection AddRuntimeTracing(this IServiceCollection services) =>
        services.ConfigureOpenTelemetryTracerProvider(tracing => tracing.AddSource([.. Sources]));
}
