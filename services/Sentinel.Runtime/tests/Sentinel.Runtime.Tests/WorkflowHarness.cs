using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Sentinel.Contracts.Investigations;
using Sentinel.Contracts.Messaging;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Contracts.Remediation;
using Sentinel.Runtime.Application;
using Sentinel.Runtime.Application.Incidents;
using Sentinel.Runtime.Domain.Incidents;
using Sentinel.Runtime.Infrastructure.Workflows;
using Shouldly;
using Temporalio.Client;
using Temporalio.Worker;
using Temporalio.Workflows;
using ContractDirection = Sentinel.Contracts.Investigations.EvidenceDirection;
using Sentinel.Runtime.Application.Abstractions.Runtime;
using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.Runtime.Application.BusinessProcesses.Remediation;
using Sentinel.Runtime.Application.Detection;
using Sentinel.Runtime.Application.Options;
using Sentinel.Runtime.Domain.Incidents.Events;
using Sentinel.Runtime.Application.Projections;

namespace Sentinel.Runtime.Tests;

/// <summary>Runtime's steps and inbound handlers wired to a real Temporal server on a task queue of its own.</summary>
internal sealed class WorkflowHarness : IDisposable
{
    private readonly string _taskQueue;

    public WorkflowHarness(string address, Action<RuntimeOptions>? configure = null)
    {
        _taskQueue = $"test-{Guid.NewGuid():N}";
        Address = address;
        Runtime = new InMemoryRuntime(services =>
        {
            services.AddOptions<RuntimeOptions>().Configure(options =>
            {
                options.ObservationWindow = TimeSpan.FromSeconds(2);
                options.EvaluationLag = TimeSpan.FromSeconds(1);
                options.EvaluationTimeout = TimeSpan.FromSeconds(10);
                options.InvestigationTimeout = TimeSpan.FromSeconds(30);
                options.Workflow.RecoveryRetryInterval = TimeSpan.FromSeconds(1);
                configure?.Invoke(options);
            });
            services.AddSingleton<IIncidentWorkflows>(provider => new RedirectableWorkflows(() => Address, _taskQueue, provider.GetRequiredService<IOptions<RuntimeOptions>>()));
        });
    }

    public string Address { get; private set; }

    public InMemoryRuntime Runtime { get; }

    /// <summary>The same incidents, served from now on by another Temporal server, as after restoring a backup.</summary>
    public void UseServer(string address) => Address = address;

    public WorkerRun StartWorker(bool legacyWorkflow = false)
    {
        ITemporalClient client = TemporalClient.ConnectAsync(new TemporalClientConnectOptions(Address)).GetAwaiter().GetResult();
        TemporalWorkerOptions options = new TemporalWorkerOptions(_taskQueue).AddAllActivities(new IncidentActivities(Runtime.Services.GetRequiredService<IServiceScopeFactory>()));
        options = legacyWorkflow ? options.AddWorkflow<IncidentWorkflowTests.LegacyIncidentWorkflow>() : options.AddWorkflow<IncidentWorkflow>();
        return new WorkerRun(new TemporalWorker(client, options)).Start();
    }

    /// <summary>Detects an incident, waits for the investigation it requests and reports findings as AgentWorker would.</summary>
    public async Task<IncidentId> OpenAndInvestigateAsync()
    {
        await Runtime.Correlation.SaveChangeAsync(InMemoryRuntime.Tenant, InMemoryRuntime.Environment, IncidentScenario.Deployment("checkout-service", "v2.4.1", "v2.4.0", -60, -40, "d1"), CancellationToken.None);
        await Runtime.InScopeAsync(provider => provider.GetRequiredService<DetectionService>().HandleAsync(InMemoryRuntime.Burn(0, 3, _taskQueue), CancellationToken.None));
        await Runtime.InScopeAsync(provider => provider.GetRequiredService<DetectionService>().HandleAsync(InMemoryRuntime.Burn(5, 4, _taskQueue), CancellationToken.None));
        IncidentId incidentId = (await Runtime.Store.ListAsync(10, CancellationToken.None)).Single().IncidentId;

        InvestigateIncident request = await Eventually(() => Runtime.Outbox.Of<InvestigateIncident>().FirstOrDefault(), "an investigation is requested");
        await Runtime.InScopeAsync(provider => provider.GetRequiredService<IRequestDispatcher>().HandleRequestAsync(new RecordInvestigationReportRequest(incidentId, Report(request).ToReport()), CancellationToken.None));
        return incidentId;
    }

    public async Task WaitForStateAsync(IncidentId incidentId, IncidentState state) =>
        await Eventually(async () => (await Runtime.Owner.GetAsync(incidentId, CancellationToken.None))?.State == state ? (object)state : null, $"the incident is {state}");

    public async Task ApproveAsync(IncidentId incidentId)
    {
        RemediationPlan plan = Runtime.Outbox.Of<RemediationPlanProposed>().Single(proposed => proposed.IncidentId == incidentId).Plan;
        await Runtime.InScopeAsync(provider => provider.GetRequiredService<IRequestDispatcher>().HandleRequestAsync(
            new RecordApprovalRequest(incidentId, plan.PlanId, plan.PlanVersion, plan.PlanHash, "responder@example.test", TestDecisions.Approve(incidentId, plan.PlanId, plan.PlanVersion, plan.PlanHash, "responder@example.test", IncidentScenario.Start)),
            CancellationToken.None));
    }

    public async Task RejectAsync(IncidentId incidentId)
    {
        RemediationPlan plan = Runtime.Outbox.Of<RemediationPlanProposed>().Single(proposed => proposed.IncidentId == incidentId).Plan;
        await Runtime.InScopeAsync(provider => provider.GetRequiredService<IRequestDispatcher>().HandleRequestAsync(
            new RecordRejectionRequest(incidentId, plan.PlanId, plan.PlanVersion, "responder@example.test", "Not this change.", TestDecisions.Reject(incidentId, plan.PlanId, plan.PlanVersion, "responder@example.test", IncidentScenario.Start)),
            CancellationToken.None));
    }

    /// <summary>Plays Executor: wait for the request, confirm the start, report the outcome.</summary>
    public async Task ExecuteAsync(IncidentId incidentId, ExecutionOutcome outcome)
    {
        ExecuteRemediation request = await Eventually(() => Runtime.Outbox.Of<ExecuteRemediation>().FirstOrDefault(execute => execute.IncidentId == incidentId), "execution is requested");
        await Runtime.InScopeAsync(provider => provider.GetRequiredService<IRequestDispatcher>().HandleRequestAsync(
            new ConfirmRemediationStartRequest(InMemoryRuntime.Tenant, InMemoryRuntime.Environment, incidentId, request.Plan.PlanId, request.Plan.PlanVersion, request.FencingToken), CancellationToken.None));
        await ReportExecutionAsync(incidentId, outcome);
    }

    /// <summary>Plays Executor reporting again, as its reconciliation does once an unknown outcome is determined.</summary>
    public async Task ReportExecutionAsync(IncidentId incidentId, ExecutionOutcome outcome)
    {
        ExecuteRemediation request = Runtime.Outbox.Of<ExecuteRemediation>().First(execute => execute.IncidentId == incidentId);
        string detail = outcome == ExecutionOutcome.OutcomeUnknown ? "Executor could not tell whether the rollback ran." : "Rolled back.";
        await Runtime.InScopeAsync(provider => provider.GetRequiredService<IRequestDispatcher>().HandleRequestAsync(
            new RecordExecutionResultRequest(incidentId, request.Plan.PlanId, request.Plan.PlanVersion, outcome.ToDomain(), detail), CancellationToken.None));
    }

    public async Task<string> ResultAsync(IncidentId incidentId)
    {
        TemporalClient client = await TemporalClient.ConnectAsync(new TemporalClientConnectOptions(Address));
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        return await client.GetWorkflowHandle(TemporalIncidentWorkflows.WorkflowId(incidentId.Value, 0))
            .GetResultAsync<string>(rpcOptions: new RpcOptions { CancellationToken = timeout.Token });
    }

    public void Dispose() => Runtime.Dispose();

    private static InvestigationCompleted Report(InvestigateIncident request)
    {
        var leak = HypothesisId.New();
        return new InvestigationCompleted
        {
            TenantId = request.TenantId,
            EnvironmentId = request.EnvironmentId,
            IncidentId = request.IncidentId,
            InvestigationId = request.InvestigationId,
            AgentExecutionId = AgentExecutionId.New(),
            Outcome = InvestigationOutcome.Completed,
            Hypotheses =
            [
                new HypothesisFinding { HypothesisId = leak, Statement = "The checkout v2.4.1 deployment leaks Redis connections.", CausalChain = ["deploy", "leak"], SuspectedChangeObservationId = ObservationId.From("deploy-controller:deployment:d1:started") },
                new HypothesisFinding { HypothesisId = HypothesisId.New(), Statement = "Load changed.", CausalChain = ["load"] }
            ],
            Evidence =
            [
                new EvidenceFinding
                {
                    EvidenceId = EvidenceId.New(),
                    Statement = "Connections rose after the deploy.",
                    Direction = ContractDirection.Supports,
                    Strength = 0.85,
                    SourceObservationIds = [ObservationId.From("prometheus:redis_connections:checkout-service:1"), ObservationId.From("prometheus:redis_connections:checkout-service:2")],
                    HypothesisIds = [leak],
                    EventTimeRange = new Sentinel.Contracts.TimeRange(IncidentScenario.Start.AddSeconds(-30), IncidentScenario.Start),
                    ToolCallId = "call_redis_connections"
                }
            ],
            Model = new ModelMetadata("sentinel-test-double", "deterministic-analyst", "1", "investigator-v1"),
            CompletedAt = IncidentScenario.Start
        };
    }

    private static Task<T> Eventually<T>(Func<T?> read, string what)
        where T : class =>
        Eventually(() => Task.FromResult(read()), what);

    private static async Task<T> Eventually<T>(Func<Task<T?>> read, string what)
        where T : class
    {
        DateTime deadline = DateTime.UtcNow + TimeSpan.FromMinutes(2);
        while (true)
        {
            if (await read() is { } value)
            {
                return value;
            }

            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException($"Timed out waiting until {what}.");
            }

            await Task.Delay(TimeSpan.FromMilliseconds(200));
        }
    }
}
