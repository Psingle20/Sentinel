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

namespace Sentinel.Runtime.Tests;

/// <summary>
/// The ADR-005 Phase 03 acceptance tests: the production incident workflow on a real Temporal
/// server, driving Runtime's real steps over in-memory stores. AgentWorker, Api and Executor are
/// played by the test through Runtime's own inbound handlers.
/// </summary>
[Collection(TemporalCollection.Name)]
[Trait("Category", "Integration")]
public sealed class IncidentWorkflowTests(TemporalFixture temporal)
{
    [Fact]
    public async Task An_approved_incident_runs_through_execution_and_a_durable_recovery_timer_to_resolution()
    {
        using var harness = new WorkflowHarness(temporal.Address);
        await using WorkerRun worker = harness.StartWorker();

        IncidentId incidentId = await harness.OpenAndInvestigateAsync();
        await harness.WaitForStateAsync(incidentId, IncidentState.AwaitingApproval);
        await harness.ApproveAsync(incidentId);
        await harness.ExecuteAsync(incidentId, ExecutionOutcome.ActionVerified);

        (await harness.ResultAsync(incidentId)).ShouldBe("recovery-judged");
        (await harness.Runtime.Owner.GetAsync(incidentId, CancellationToken.None))!.Recovery.ShouldBe(RecoveryResult.Recovered);
        harness.Runtime.Outbox.Of<ExecuteRemediation>().ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Duplicate_approval_signals_request_execution_once()
    {
        using var harness = new WorkflowHarness(temporal.Address);
        await using WorkerRun worker = harness.StartWorker();

        IncidentId incidentId = await harness.OpenAndInvestigateAsync();
        await harness.WaitForStateAsync(incidentId, IncidentState.AwaitingApproval);
        await harness.ApproveAsync(incidentId);
        await harness.ApproveAsync(incidentId);
        await harness.WaitForStateAsync(incidentId, IncidentState.Remediating);
        await harness.ApproveAsync(incidentId);

        await harness.ExecuteAsync(incidentId, ExecutionOutcome.ActionVerified);
        await harness.ResultAsync(incidentId);
        harness.Runtime.Outbox.Of<ExecuteRemediation>().ShouldHaveSingleItem();
    }

    [Fact]
    public async Task A_rejection_ends_the_run_and_nothing_executes()
    {
        using var harness = new WorkflowHarness(temporal.Address);
        await using WorkerRun worker = harness.StartWorker();

        IncidentId incidentId = await harness.OpenAndInvestigateAsync();
        await harness.WaitForStateAsync(incidentId, IncidentState.AwaitingApproval);
        await harness.RejectAsync(incidentId);

        (await harness.ResultAsync(incidentId)).ShouldBe("rejected");
        harness.Runtime.Outbox.Of<ExecuteRemediation>().ShouldBeEmpty();
    }

    [Fact]
    public async Task An_approval_that_never_comes_expires_and_escalates()
    {
        using var harness = new WorkflowHarness(temporal.Address, options => options.Workflow.ApprovalTimeout = TimeSpan.FromSeconds(3));
        await using WorkerRun worker = harness.StartWorker();

        IncidentId incidentId = await harness.OpenAndInvestigateAsync();

        (await harness.ResultAsync(incidentId)).ShouldBe("approval-expired");
        (await harness.Runtime.Owner.GetAsync(incidentId, CancellationToken.None))!.State.ShouldBe(IncidentState.Escalated);
    }

    [Fact]
    public async Task A_worker_killed_mid_run_resumes_from_its_last_completed_step()
    {
        using var harness = new WorkflowHarness(temporal.Address);
        IncidentId incidentId;
        await using (WorkerRun first = harness.StartWorker())
        {
            incidentId = await harness.OpenAndInvestigateAsync();
            await harness.WaitForStateAsync(incidentId, IncidentState.AwaitingApproval);
        }

        // Approved while no worker runs: the signal waits in Temporal.
        await harness.ApproveAsync(incidentId);
        await using WorkerRun second = harness.StartWorker();
        await harness.ExecuteAsync(incidentId, ExecutionOutcome.ActionVerified);

        (await harness.ResultAsync(incidentId)).ShouldBe("recovery-judged");
        harness.Runtime.Outbox.Of<InvestigateIncident>().ShouldHaveSingleItem();
        harness.Runtime.Policy.Calls.ShouldBe(1);
    }

    [Fact]
    public async Task An_in_flight_run_survives_a_versioned_workflow_change()
    {
        using var harness = new WorkflowHarness(temporal.Address);
        IncidentId incidentId;
        await using (WorkerRun legacy = harness.StartWorker(legacyWorkflow: true))
        {
            incidentId = await harness.OpenAndInvestigateAsync();
            await harness.WaitForStateAsync(incidentId, IncidentState.AwaitingApproval);
        }

        // The current code adds a step guarded by Workflow.Patched; replaying the legacy history skips it.
        await using WorkerRun current = harness.StartWorker();
        await harness.ApproveAsync(incidentId);
        await harness.ExecuteAsync(incidentId, ExecutionOutcome.ActionVerified);

        (await harness.ResultAsync(incidentId)).ShouldBe("recovery-judged");
    }

    [Fact]
    public async Task A_Temporal_server_restart_loses_no_accepted_state_or_progress()
    {
        using var harness = new WorkflowHarness(temporal.Address);
        await using WorkerRun worker = harness.StartWorker();
        IncidentId incidentId = await harness.OpenAndInvestigateAsync();
        await harness.WaitForStateAsync(incidentId, IncidentState.AwaitingApproval);

        await temporal.Temporal.StopAsync();
        await temporal.Temporal.StartAsync();
        await TemporalFixture.WaitUntilServingAsync(temporal.Address);

        await harness.ApproveAsync(incidentId);
        await harness.ExecuteAsync(incidentId, ExecutionOutcome.ActionVerified);
        (await harness.ResultAsync(incidentId)).ShouldBe("recovery-judged");
    }

    [Fact]
    public async Task A_restored_backup_of_the_Temporal_database_resumes_in_flight_runs()
    {
        IncidentId incidentId;
        using var harness = new WorkflowHarness(temporal.Address);
        await using (WorkerRun worker = harness.StartWorker())
        {
            incidentId = await harness.OpenAndInvestigateAsync();
            await harness.WaitForStateAsync(incidentId, IncidentState.AwaitingApproval);
        }

        // Back up both Temporal databases and restore them into a new, empty server: nothing is dropped.
        (await temporal.Postgres.ExecAsync(["sh", "-c", "pg_dumpall -U postgres > /tmp/temporal.sql"])).ExitCode.ShouldBe(0);
        string backup = (await temporal.Postgres.ExecAsync(["cat", "/tmp/temporal.sql"])).Stdout;
        await using DotNet.Testcontainers.Containers.IContainer restored = temporal.PostgresContainer("postgres-restored");
        await restored.StartAsync();
        await restored.CopyAsync(System.Text.Encoding.UTF8.GetBytes(backup), "/tmp/temporal.sql");
        (await restored.ExecAsync(["sh", "-c", "psql -U postgres -v ON_ERROR_STOP=0 -f /tmp/temporal.sql > /dev/null"])).ExitCode.ShouldBe(0);
        (DotNet.Testcontainers.Containers.IContainer server, string address) = await temporal.StartTemporalAsync("postgres-restored", TemporalFixture.FreePort());
        await using (server)
        {
            harness.UseServer(address);
            await using WorkerRun worker = harness.StartWorker();
            await harness.ApproveAsync(incidentId);
            await harness.ExecuteAsync(incidentId, ExecutionOutcome.ActionVerified);

            (await harness.ResultAsync(incidentId)).ShouldBe("recovery-judged");
        }
    }

    /// <summary>The production workflow before <see cref="IncidentWorkflow.RereadBeforeApprovalPatch"/>, for the upgrade test.</summary>
    [Workflow(IncidentWorkflow.Name)]
    public sealed class LegacyIncidentWorkflow
    {
        private static readonly ActivityOptions Step = new() { StartToCloseTimeout = TimeSpan.FromMinutes(1) };
        private readonly HashSet<Guid> _reported = [];
        private bool _answered;

        [WorkflowRun]
        public async Task<string> RunAsync(IncidentWorkflowInput input)
        {
            ArgumentNullException.ThrowIfNull(input);

            Guid incidentId = input.IncidentId;
            IReadOnlyList<Guid> investigations = await Workflow.ExecuteActivityAsync((IncidentActivities activities) => activities.RequestInvestigationsAsync(incidentId), Step);
            StepSummary current = await Workflow.ExecuteActivityAsync((IncidentActivities activities) => activities.GetAsync(incidentId), Step);
            _reported.UnionWith(current.ReportedInvestigationIds);
            await Workflow.WaitConditionAsync(() => investigations.All(_reported.Contains), input.Timings.InvestigationTimeout);
            await Workflow.ExecuteActivityAsync((IncidentActivities activities) => activities.DecideAsync(incidentId), Step);
            await Workflow.ExecuteActivityAsync((IncidentActivities activities) => activities.PlanAsync(incidentId), Step);
            await Workflow.WaitConditionAsync(() => _answered, input.Timings.ApprovalTimeout);
            return "legacy";
        }

        [WorkflowSignal]
        public Task SignalAsync(IncidentWorkflowSignal signal)
        {
            ArgumentNullException.ThrowIfNull(signal);

            if (signal.Kind == "InvestigationReported" && Guid.TryParse(signal.Reference, out Guid id))
            {
                _reported.Add(id);
            }

            _answered |= signal.Kind is "ApprovalRecorded" or "PlanRejected";
            return Task.CompletedTask;
        }
    }
}

/// <summary>A Temporal worker running in the background until disposed, as a crashed or stopped Runtime would stop polling.</summary>
internal sealed class WorkerRun(TemporalWorker worker) : IAsyncDisposable
{
    private readonly CancellationTokenSource _stop = new();
    private Task? _running;

    public WorkerRun Start()
    {
        _running = worker.ExecuteAsync(_stop.Token);
        return this;
    }

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        try
        {
            await _running!;
        }
        catch (OperationCanceledException)
        {
            // A stopped worker ends by cancellation.
        }

        worker.Dispose();
        _stop.Dispose();
    }
}

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
        await Runtime.InScopeAsync(provider => provider.GetRequiredService<RemediationControlService>().HandleInvestigationAsync(Report(request), CancellationToken.None));
        return incidentId;
    }

    public async Task WaitForStateAsync(IncidentId incidentId, IncidentState state) =>
        await Eventually(async () => (await Runtime.Owner.GetAsync(incidentId, CancellationToken.None))?.State == state ? (object)state : null, $"the incident is {state}");

    public async Task ApproveAsync(IncidentId incidentId)
    {
        RemediationPlan plan = Runtime.Outbox.Of<RemediationPlanProposed>().Single(proposed => proposed.IncidentId == incidentId).Plan;
        await Runtime.InScopeAsync(provider => provider.GetRequiredService<RemediationControlService>().HandleApprovalAsync(new RecordApproval
        {
            TenantId = InMemoryRuntime.Tenant,
            EnvironmentId = InMemoryRuntime.Environment,
            IncidentId = incidentId,
            PlanId = plan.PlanId,
            PlanVersion = plan.PlanVersion,
            PlanHash = plan.PlanHash,
            ApproverId = "responder@example.test",
            Attestation = "header.claims.signature",
            ApprovedAt = IncidentScenario.Start
        }, CancellationToken.None));
    }

    public async Task RejectAsync(IncidentId incidentId)
    {
        RemediationPlan plan = Runtime.Outbox.Of<RemediationPlanProposed>().Single(proposed => proposed.IncidentId == incidentId).Plan;
        await Runtime.InScopeAsync(provider => provider.GetRequiredService<RemediationControlService>().HandleRejectionAsync(new RecordRejection
        {
            TenantId = InMemoryRuntime.Tenant,
            EnvironmentId = InMemoryRuntime.Environment,
            IncidentId = incidentId,
            PlanId = plan.PlanId,
            PlanVersion = plan.PlanVersion,
            RejectedBy = "responder@example.test",
            Reason = "Not this change.",
            RejectedAt = IncidentScenario.Start
        }, CancellationToken.None));
    }

    /// <summary>Plays Executor: wait for the request, confirm the start, report the outcome.</summary>
    public async Task ExecuteAsync(IncidentId incidentId, ExecutionOutcome outcome)
    {
        ExecuteRemediation request = await Eventually(() => Runtime.Outbox.Of<ExecuteRemediation>().FirstOrDefault(execute => execute.IncidentId == incidentId), "execution is requested");
        await Runtime.InScopeAsync(provider => provider.GetRequiredService<RemediationControlService>().ConfirmStartAsync(
            new RemediationStartRequest(InMemoryRuntime.Tenant, InMemoryRuntime.Environment, incidentId, request.Plan.PlanId, request.Plan.PlanVersion, request.FencingToken), CancellationToken.None));
        await Runtime.InScopeAsync(provider => provider.GetRequiredService<RemediationControlService>().HandleExecutedAsync(new RemediationExecuted
        {
            TenantId = InMemoryRuntime.Tenant,
            EnvironmentId = InMemoryRuntime.Environment,
            IncidentId = incidentId,
            PlanId = request.Plan.PlanId,
            PlanVersion = request.Plan.PlanVersion,
            Outcome = outcome,
            Detail = "Rolled back.",
            CompletedAt = IncidentScenario.Start
        }, CancellationToken.None));
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

/// <summary>The production <see cref="TemporalIncidentWorkflows"/>, re-created when the harness moves to another server.</summary>
internal sealed class RedirectableWorkflows(Func<string> address, string taskQueue, IOptions<RuntimeOptions> runtime) : IIncidentWorkflows
{
    private readonly object _gate = new();
    private (string Address, TemporalIncidentWorkflows Workflows)? _current;

    public Task EnsureStartedAsync(IncidentSnapshot incident, CancellationToken cancellationToken) => Current().EnsureStartedAsync(incident, cancellationToken);

    public Task SignalAsync(IncidentSnapshot incident, IncidentSignal signal, CancellationToken cancellationToken) => Current().SignalAsync(incident, signal, cancellationToken);

    private TemporalIncidentWorkflows Current()
    {
        lock (_gate)
        {
            string target = address();
            if (_current is not { } current || current.Address != target)
            {
                IOptions<TemporalOptions> options = Options.Create(new TemporalOptions { Address = target, TaskQueue = taskQueue });
                var clients = new TemporalClientProvider(options, Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance);
                _current = (target, new TemporalIncidentWorkflows(clients, options, runtime, Microsoft.Extensions.Logging.Abstractions.NullLogger<TemporalIncidentWorkflows>.Instance));
            }

            return _current.Value.Workflows;
        }
    }
}
