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
using Sentinel.Runtime.Application.Options;

namespace Sentinel.Runtime.Infrastructure.Workflows;

/// <summary><see cref="IIncidentWorkflows"/> on Temporal: one workflow ID per incident cycle, never reused.</summary>
internal sealed class TemporalIncidentWorkflows(
    TemporalClientProvider clients,
    IOptions<TemporalOptions> temporal,
    IOptions<RuntimeOptions> runtime,
    ILogger<TemporalIncidentWorkflows> logger) : IIncidentWorkflows
{
    public static string WorkflowId(Guid incidentId, int cycle) => $"incident-{incidentId}-cycle-{cycle}";

    public async Task EnsureStartedAsync(IncidentSnapshot incident, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(incident);

        ITemporalClient client = await clients.GetAsync(cancellationToken);
        RuntimeOptions settings = runtime.Value;
        var input = new IncidentWorkflowInput(incident.IncidentId.Value, incident.Cycle, new WorkflowTimings(
            settings.InvestigationTimeout + settings.Investigation.ReportGrace,
            settings.Investigation.Challenge,
            settings.Workflow.ApprovalTimeout,
            settings.Workflow.ExecutionTimeout,
            settings.EvaluationLag,
            settings.EvaluationTimeout - settings.EvaluationLag,
            settings.Workflow.RecoveryRetryInterval));
        try
        {
            await client.StartWorkflowAsync(
                (IncidentWorkflow workflow) => workflow.RunAsync(input),
                new Temporalio.Client.WorkflowOptions(WorkflowId(incident.IncidentId.Value, incident.Cycle), temporal.Value.TaskQueue)
                {
                    // A finished cycle is never started again; a reopened incident gets a new cycle.
                    IdReusePolicy = WorkflowIdReusePolicy.RejectDuplicate,
                    Rpc = new RpcOptions { CancellationToken = cancellationToken }
                });
            logger.LogInformation("Started workflow for incident {IncidentId} cycle {Cycle}", incident.IncidentId, incident.Cycle);
        }
        catch (WorkflowAlreadyStartedException)
        {
            // Already running or finished: starting is idempotent.
        }
    }

    public async Task SignalAsync(IncidentSnapshot incident, IncidentSignal signal, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(incident);
        ArgumentNullException.ThrowIfNull(signal);

        ITemporalClient client = await clients.GetAsync(cancellationToken);
        WorkflowHandle<IncidentWorkflow> handle = client.GetWorkflowHandle<IncidentWorkflow>(WorkflowId(incident.IncidentId.Value, incident.Cycle));
        try
        {
            await handle.SignalAsync(workflow => workflow.SignalAsync(new IncidentWorkflowSignal(signal.Kind.ToString(), signal.Reference)), new WorkflowSignalOptions { Rpc = new RpcOptions { CancellationToken = cancellationToken } });
        }
        catch (RpcException exception) when (exception.Code == RpcException.StatusCode.NotFound)
        {
            // The run already finished, or never started for an incident that needed no workflow.
            logger.LogDebug(exception, "No running workflow for incident {IncidentId} cycle {Cycle} to signal {Signal}", incident.IncidentId, incident.Cycle, signal.Kind);
        }
    }
}
