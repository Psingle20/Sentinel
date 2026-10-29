using System.Net.Http.Json;
using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Infrastructure.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Sentinel.Contracts.Incidents;
using Sentinel.Contracts.Investigations;
using Sentinel.Contracts.Messaging;
using Sentinel.Contracts.Observations;
using Sentinel.Contracts.Queries;
using Sentinel.Contracts.Remediation;
using Sentinel.Runtime.Application;
using Sentinel.Runtime.Application.Incidents;
using Sentinel.Runtime.Application.Abstractions.Runtime;
using Sentinel.Runtime.Application.Abstractions.Persistence;

namespace Sentinel.Runtime.Infrastructure.Workflows;

/// <summary>
/// Makes sure every open incident's cycle has its workflow, for example after Temporal was down
/// when the incident opened. Starting is idempotent, so this only repairs and never duplicates.
/// </summary>
internal sealed class WorkflowReconciler(IServiceScopeFactory scopes, IIncidentOwner owner, IIncidentWorkflows workflows, TimeProvider time, ILogger<WorkflowReconciler> logger) : BackgroundService
{
    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(Interval, time, stoppingToken);
            try
            {
                IReadOnlyList<IncidentSummary> incidents;
                await using (AsyncServiceScope scope = scopes.CreateAsyncScope())
                {
                    incidents = await scope.ServiceProvider.GetRequiredService<IIncidentStore>().ListAsync(200, stoppingToken);
                }

                foreach (IncidentSummary summary in incidents.Where(incident => incident.Status is not (IncidentStatus.Resolved or IncidentStatus.Escalated)))
                {
                    if (await owner.GetAsync(summary.IncidentId, stoppingToken) is { IsOpen: true } snapshot)
                    {
                        await workflows.EnsureStartedAsync(snapshot, stoppingToken);
                    }
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException || !stoppingToken.IsCancellationRequested)
            {
                logger.LogWarning(exception, "Workflow reconciliation failed; it is retried in {Interval}", Interval);
            }
        }
    }
}
