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

namespace Sentinel.Runtime.Infrastructure.Workflows;

/// <summary>Configured as <c>Runtime:Temporal</c>; the address comes from the orchestrator.</summary>
public sealed class TemporalOptions
{
    public const string Section = "Runtime:Temporal";

    /// <summary><c>host:port</c> of the Temporal frontend.</summary>
    public string Address { get; set; } = string.Empty;

    public string Namespace { get; set; } = "default";

    public string TaskQueue { get; set; } = IncidentWorkflow.TaskQueue;
}

/// <summary>
/// One Temporal client for the process, connected on first use. A failed connection is not cached,
/// so callers fail fast while Temporal is down and succeed as soon as it is back; message handlers
/// that need it are retried by the event transport.
/// </summary>
public sealed class TemporalClientProvider(IOptions<TemporalOptions> options, ILoggerFactory loggers) : IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private ITemporalClient? _client;

    public bool IsConnected => _client is not null;

    public async Task<ITemporalClient> GetAsync(CancellationToken cancellationToken)
    {
        if (_client is { } connected)
        {
            return connected;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            _client ??= await TemporalClient.ConnectAsync(new TemporalClientConnectOptions(options.Value.Address)
            {
                Namespace = options.Value.Namespace,
                LoggerFactory = loggers,
                Interceptors = [new TracingInterceptor()]
            });
            return _client;
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose() => _gate.Dispose();
}

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

/// <summary>Runtime is ready only once its workflow worker polls Temporal.</summary>
internal sealed class TemporalHealthCheck(TemporalWorkerHost worker) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default) =>
        Task.FromResult(worker.IsRunning ? HealthCheckResult.Healthy() : HealthCheckResult.Unhealthy("The Temporal worker is not connected."));
}

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
