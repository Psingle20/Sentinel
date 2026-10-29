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

/// <summary>
/// Runs the incident workflow and its activities inside Runtime (ADR-005: no separate deployable).
/// It connects with backoff, so Runtime starts and serves while Temporal is still starting; a lost
/// connection restarts the worker, and Temporal resumes every in-flight run from its history.
/// </summary>
internal sealed class TemporalWorkerHost(
    TemporalClientProvider clients,
    IncidentActivities activities,
    IOptions<TemporalOptions> options,
    ILogger<TemporalWorkerHost> logger) : BackgroundService
{
    public bool IsRunning { get; private set; }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var delay = TimeSpan.FromSeconds(1);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                ITemporalClient client = await clients.GetAsync(stoppingToken);
                using var worker = new TemporalWorker(client, new TemporalWorkerOptions(options.Value.TaskQueue)
                    .AddWorkflow<IncidentWorkflow>()
                    .AddAllActivities(activities));
                IsRunning = true;
                delay = TimeSpan.FromSeconds(1);
                logger.LogInformation("Temporal worker polling {TaskQueue} on {Address}", options.Value.TaskQueue, options.Value.Address);
                await worker.ExecuteAsync(stoppingToken);
            }
            catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
            {
                IsRunning = false;
                logger.LogWarning(exception, "Temporal worker stopped; reconnecting in {Delay}", delay);
                await Task.Delay(delay, stoppingToken);
                delay = TimeSpan.FromTicks(Math.Min(delay.Ticks * 2, TimeSpan.FromSeconds(30).Ticks));
            }
            finally
            {
                IsRunning = false;
            }
        }
    }
}
