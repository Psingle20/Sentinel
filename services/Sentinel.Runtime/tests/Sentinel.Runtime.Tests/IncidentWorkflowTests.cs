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
