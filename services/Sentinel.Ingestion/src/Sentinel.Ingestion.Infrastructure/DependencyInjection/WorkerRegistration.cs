using Microsoft.Extensions.DependencyInjection;
using Sentinel.Ingestion.Application.Abstractions.Diagnostics;
using Sentinel.Ingestion.Infrastructure.Diagnostics;
using Sentinel.Ingestion.Infrastructure.Workers;

namespace Sentinel.Ingestion.Infrastructure.DependencyInjection;

internal static class WorkerRegistration
{
    /// <summary>The worker that pulls every configured source, and the telemetry it and the ingestion cycle emit.</summary>
    public static IServiceCollection AddIngestionWorker(this IServiceCollection services)
    {
        services.AddSingleton<IngestionTelemetry>();
        services.AddSingleton<IIngestionMetrics>(provider => provider.GetRequiredService<IngestionTelemetry>());
        services.AddHostedService<IngestionWorker>();
        return services;
    }
}
