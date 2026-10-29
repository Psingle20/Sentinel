using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sentinel.Runtime.Application;
using Sentinel.Runtime.Application.Incidents;
using Temporalio.Api.Enums.V1;
using Temporalio.Client;
using Temporalio.Exceptions;
using Temporalio.Extensions.OpenTelemetry;
using Temporalio.Worker;
using Sentinel.Runtime.Application.Abstractions.Runtime;

namespace Sentinel.Runtime.Infrastructure.Workflows;

/// <summary>Runtime is ready only once its workflow worker polls Temporal.</summary>
internal sealed class TemporalHealthCheck(TemporalWorkerHost worker) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default) =>
        Task.FromResult(worker.IsRunning ? HealthCheckResult.Healthy() : HealthCheckResult.Unhealthy("The Temporal worker is not connected."));
}
