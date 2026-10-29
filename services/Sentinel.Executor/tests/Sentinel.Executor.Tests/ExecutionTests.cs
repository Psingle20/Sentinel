using System.Security.Cryptography;
using CleanArchitecture.BuildingBlocks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Contracts.Remediation;
using Sentinel.Executor.Application;
using Sentinel.Executor.Application.BusinessTasks.Commands.Execution;
using Sentinel.Executor.Domain.Executions;
using Sentinel.Executor.Domain.Policy;
using Shouldly;
using MicrosoftOptions = Microsoft.Extensions.Options.Options;
using Sentinel.Executor.Application.Abstractions.Remediation;
using Sentinel.Executor.Application.Options;
using Sentinel.Executor.Application.Services;

namespace Sentinel.Executor.Tests;

public sealed class ExecutionTests : IDisposable
{
    private readonly TestSigner _signer = new();
    private readonly FakeTimeProvider _time = new(Commands.Now);
    private readonly InMemoryExecutionStore _store = new();
    private readonly FakeStartConfirmation _start = new();
    private readonly FakeRollbackTarget _target = new();
    private readonly RecordingOutbox _outbox = new();

    public void Dispose() => _signer.Dispose();

    private readonly FakePolicyStore _policy = new();
    private readonly FakeControlStore _controls = new();
    private readonly FakeBatchTarget _batch = new();

    private ExecutionService Service()
    {
        var options = new ExecutorOptions { VerifyTimeout = TimeSpan.FromSeconds(1) };
        Microsoft.Extensions.Options.IOptions<ExecutorOptions> wrapped = MicrosoftOptions.Create(options);
        return new ExecutionService(
            _store,
            new AttestationVerifier(_signer, _time),
            new PolicyService(_policy, _controls, wrapped, _time),
            _start,
            [_target, _batch],
            _outbox,
            new NoUnitOfWork(),
            _time,
            wrapped,
            NullLogger<ExecutionService>.Instance);
    }

    // The consumer's path: Runtime's message arrives as a command, whose handler verifies and reserves it and hands it to the state machine.
    private Task<Result> ExecuteAsync(ExecuteRemediation command) =>
        new ExecuteRemediationCommandHandler(_store, Service(), new InFlightExecutions(), new NoUnitOfWork(), _time, NullLogger<ExecuteRemediationCommandHandler>.Instance).Handle(new ExecuteRemediationCommand(command), CancellationToken.None);

    private ExecutionOutcome LastOutcome() => _outbox.Messages.OfType<RemediationExecuted>().Last().Outcome;

    [Fact]
    public async Task An_approved_plan_is_confirmed_executed_once_and_verified()
    {
        await ExecuteAsync(Commands.Execute(_signer));

        _start.Calls.ShouldBe(1);
        _target.Executions.ShouldBe(1);
        LastOutcome().ShouldBe(ExecutionOutcome.ActionVerified);
        _store.All.Single().History.Select(step => step.To).ShouldBe([
            ExecutionState.Reserved, ExecutionState.StartConfirmed, ExecutionState.Executing, ExecutionState.ActionVerified]);
    }

    [Fact]
    public async Task A_repeated_request_reports_its_result_again_without_executing_again()
    {
        ExecuteRemediation command = Commands.Execute(_signer);

        await ExecuteAsync(command);
        await ExecuteAsync(command);

        _target.Executions.ShouldBe(1);
        _outbox.Messages.OfType<RemediationExecuted>().Count().ShouldBe(2);
    }

    [Fact]
    public async Task An_unauthenticated_command_binds_no_idempotency_key_and_reaches_Runtime_with_nothing()
    {
        // Review F-006: a command that is not the Gateway's must not occupy the predictable key the genuine one arrives under.
        using var attacker = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        ExecuteRemediation genuine = Commands.Execute(_signer);
        ExecuteRemediation forged = genuine with { Attestation = _signer.Sign(Commands.Claims(genuine.IncidentId, genuine.Plan), key: attacker) };

        await ExecuteAsync(forged);
        _outbox.Messages.OfType<RemediationExecuted>().ShouldBeEmpty();
        _target.Executions.ShouldBe(0);

        await ExecuteAsync(genuine);

        _target.Executions.ShouldBe(1);
        LastOutcome().ShouldBe(ExecutionOutcome.ActionVerified);
        _store.All.Count.ShouldBe(2);
        _store.All.ShouldContain(request => request.IdempotencyKey.Contains(ExecuteRemediationCommandHandler.UnverifiedKeySuffix, StringComparison.Ordinal) && request.State == ExecutionState.Rejected);
        _store.All.ShouldContain(request => request.IdempotencyKey == genuine.IdempotencyKey && request.State == ExecutionState.ActionVerified);
    }

    [Fact]
    public async Task An_attestation_cannot_be_used_twice()
    {
        ExecuteRemediation first = Commands.Execute(_signer);
        ExecuteRemediation replay = first with { IdempotencyKey = first.IdempotencyKey + ":replay", FencingToken = first.FencingToken + 1 };

        await ExecuteAsync(first);
        await ExecuteAsync(replay);

        _target.Executions.ShouldBe(1);
        LastOutcome().ShouldBe(ExecutionOutcome.Rejected);
    }

    [Fact]
    public async Task A_fencing_token_lower_than_one_already_seen_is_rejected()
    {
        var incident = IncidentId.New();
        await ExecuteAsync(Commands.Execute(_signer, fencingToken: 9, key: "k1", incident: incident));
        _target.Serving = "v2.4.1";
        _target.Operation = OperationStatus.NotFound;

        await ExecuteAsync(Commands.Execute(_signer, fencingToken: 8, key: "k2", incident: incident));

        LastOutcome().ShouldBe(ExecutionOutcome.Rejected);
        _target.Executions.ShouldBe(1);
    }

    [Fact]
    public async Task Without_Runtime_confirmation_nothing_runs()
    {
        _start.Confirm = false;
        await ExecuteAsync(Commands.Execute(_signer, key: "refused"));
        LastOutcome().ShouldBe(ExecutionOutcome.Aborted);

        _start.Confirm = true;
        _start.Unreachable = true;
        await ExecuteAsync(Commands.Execute(_signer, key: "unreachable"));
        LastOutcome().ShouldBe(ExecutionOutcome.Aborted);

        _target.Executions.ShouldBe(0);
    }

    [Fact]
    public async Task A_target_not_serving_the_plans_from_revision_fails_the_precondition()
    {
        _target.Serving = "v2.4.3";

        await ExecuteAsync(Commands.Execute(_signer));

        LastOutcome().ShouldBe(ExecutionOutcome.Rejected);
        _start.Calls.ShouldBe(0);
    }

    [Fact]
    public async Task A_denied_plan_is_rejected_even_with_a_valid_attestation()
    {
        RemediationPlan plan = Commands.Plan(target: "payments-service");

        await ExecuteAsync(Commands.Execute(_signer, plan));

        LastOutcome().ShouldBe(ExecutionOutcome.Rejected);
        _target.Executions.ShouldBe(0);
    }

    [Fact]
    public async Task An_ambiguous_result_becomes_outcome_unknown_and_is_reconciled_from_the_target_without_repeating_the_action()
    {
        _target.NextOutcome = AdapterOutcome.Unknown;
        await ExecuteAsync(Commands.Execute(_signer));
        LastOutcome().ShouldBe(ExecutionOutcome.OutcomeUnknown);

        // The rollback did happen at the target after all.
        _target.Serving = "v2.4.0";
        _target.Operation = OperationStatus.Completed;
        ExecutionRequest unfinished = (await _store.UnfinishedAsync(Commands.Now, CancellationToken.None)).Single();
        await Service().ReconcileAsync(unfinished, CancellationToken.None);

        LastOutcome().ShouldBe(ExecutionOutcome.ActionVerified);
        _target.Executions.ShouldBe(1);
    }

    [Fact]
    public async Task A_crash_while_executing_is_reconciled_as_failed_when_the_target_never_saw_the_action()
    {
        RemediationPlan plan = Commands.Plan();
        var request = ExecutionRequest.Receive("crashed", Commands.Tenant, Commands.Environment, IncidentId.New(), plan.PlanId, 1, plan.PlanHash, 7,
            System.Text.Json.JsonSerializer.Serialize(plan, Sentinel.Contracts.Messaging.SentinelJson.Options), Commands.Now);
        request.Reserve(AttestationId.New(), "responder@example.test", "static-2026.10.1", Commands.Now);
        request.ConfirmStart(Commands.Now);
        request.BeginExecuting(Commands.Now);
        await _store.SaveAsync(request, CancellationToken.None);

        await Service().ReconcileAsync((await _store.UnfinishedAsync(Commands.Now, CancellationToken.None)).Single(), CancellationToken.None);

        _outbox.Messages.OfType<RemediationExecuted>().Select(message => message.Outcome).ShouldBe([ExecutionOutcome.OutcomeUnknown, ExecutionOutcome.Failed]);
        _target.Executions.ShouldBe(0);
    }

    [Fact]
    public async Task A_request_interrupted_before_start_confirmation_is_aborted_with_no_side_effect()
    {
        RemediationPlan plan = Commands.Plan();
        var request = ExecutionRequest.Receive("reserved", Commands.Tenant, Commands.Environment, IncidentId.New(), plan.PlanId, 1, plan.PlanHash, 7,
            System.Text.Json.JsonSerializer.Serialize(plan, Sentinel.Contracts.Messaging.SentinelJson.Options), Commands.Now);
        request.Reserve(AttestationId.New(), "responder@example.test", "static-2026.10.1", Commands.Now);
        await _store.SaveAsync(request, CancellationToken.None);

        await Service().ReconcileAsync(request, CancellationToken.None);

        LastOutcome().ShouldBe(ExecutionOutcome.Aborted);
        _target.Executions.ShouldBe(0);
    }
    [Fact]
    public async Task A_request_interrupted_before_it_was_reserved_is_rejected_with_no_side_effect()
    {
        RemediationPlan plan = Commands.Plan();
        var request = ExecutionRequest.Receive("received", Commands.Tenant, Commands.Environment, IncidentId.New(), plan.PlanId, 1, plan.PlanHash, 7,
            System.Text.Json.JsonSerializer.Serialize(plan, Sentinel.Contracts.Messaging.SentinelJson.Options), Commands.Now);
        await _store.SaveAsync(request, CancellationToken.None);

        await Service().ReconcileAsync(request, CancellationToken.None);

        LastOutcome().ShouldBe(ExecutionOutcome.Rejected);
        _target.Executions.ShouldBe(0);
    }

    [Fact]
    public async Task A_request_still_being_handled_is_left_to_its_handler_however_slow_its_steps()
    {
        RemediationPlan plan = Commands.Plan();
        var request = ExecutionRequest.Receive("in-flight", Commands.Tenant, Commands.Environment, IncidentId.New(), plan.PlanId, 1, plan.PlanHash, 7,
            System.Text.Json.JsonSerializer.Serialize(plan, Sentinel.Contracts.Messaging.SentinelJson.Options), Commands.Now);
        await _store.SaveAsync(request, CancellationToken.None);
        var inFlight = new InFlightExecutions();
        var reconcile = new ReconcileExecutionCommandHandler(_store, Service(), inFlight, NullLogger<ReconcileExecutionCommandHandler>.Instance);

        using (inFlight.Track("in-flight"))
        {
            await reconcile.Handle(new ReconcileExecutionCommand("in-flight"), CancellationToken.None);
            (await _store.FindAsync("in-flight", CancellationToken.None))!.State.ShouldBe(ExecutionState.Received, "its handler is still inspecting the target");
            _outbox.Messages.OfType<RemediationExecuted>().ShouldBeEmpty();
        }

        // Once nothing in this process is handling it, it was left behind, as by a crash, and is reconciled.
        await reconcile.Handle(new ReconcileExecutionCommand("in-flight"), CancellationToken.None);
        LastOutcome().ShouldBe(ExecutionOutcome.Rejected);
        _target.Executions.ShouldBe(0);
    }

    [Fact]
    public async Task A_crash_after_start_confirmation_executes_the_action_exactly_once_on_reconciliation()
    {
        RemediationPlan plan = Commands.Plan();
        var request = ExecutionRequest.Receive("confirmed", Commands.Tenant, Commands.Environment, IncidentId.New(), plan.PlanId, 1, plan.PlanHash, 7,
            System.Text.Json.JsonSerializer.Serialize(plan, Sentinel.Contracts.Messaging.SentinelJson.Options), Commands.Now);
        request.Reserve(AttestationId.New(), "responder@example.test", "static-2026.10.1", Commands.Now);
        request.ConfirmStart(Commands.Now);
        await _store.SaveAsync(request, CancellationToken.None);

        await Service().ReconcileAsync((await _store.UnfinishedAsync(Commands.Now, CancellationToken.None)).Single(), CancellationToken.None);
        (await _store.UnfinishedAsync(Commands.Now, CancellationToken.None)).ShouldBeEmpty("the reconciled request is finished");

        LastOutcome().ShouldBe(ExecutionOutcome.ActionVerified);
        _target.Executions.ShouldBe(1);
    }

    [Fact]
    public async Task Every_action_runs_under_its_own_single_use_capability()
    {
        await ExecuteAsync(Commands.Execute(_signer));

        Sentinel.Executor.Domain.Capabilities.Capability capability = _target.Capabilities.ShouldHaveSingleItem();
        capability.Action.ShouldBe("rollback");
        capability.Resource.ShouldBe("checkout-service");
        capability.Scope.ShouldBe("acme/reference");
        capability.Limits["to"].ShouldBe("v2.4.0");
        (await _store.TryConsumeCapabilityAsync(capability.CapabilityId, CancellationToken.None)).ShouldBeFalse();
    }

    [Fact]
    public async Task A_suggestion_only_plan_is_never_executed_even_with_an_attestation()
    {
        RemediationPlan plan = Commands.PausePlan();

        await ExecuteAsync(Commands.Execute(_signer, plan));

        LastOutcome().ShouldBe(ExecutionOutcome.Rejected);
        _outbox.Messages.OfType<RemediationExecuted>().Last().Detail.ShouldNotBeNull().ShouldContain("SuggestionOnly");
        _batch.Active.ShouldBeTrue();
    }

    [Fact]
    public async Task The_emergency_stop_engaged_at_run_time_rejects_execution()
    {
        await _controls.SetEmergencyStopAsync(true, "operator@example.test", CancellationToken.None);

        await ExecuteAsync(Commands.Execute(_signer));

        LastOutcome().ShouldBe(ExecutionOutcome.Rejected);
        _target.Executions.ShouldBe(0);
    }

    [Fact]
    public async Task A_plan_that_fails_part_way_is_compensated_in_reverse()
    {
        _policy.Current = Autonomous();
        _target.NextOutcome = AdapterOutcome.Rejected;
        RemediationPlan plan = Commands.PausePlan(alsoRollback: true);

        await ExecuteAsync(Commands.Execute(_signer, plan));

        LastOutcome().ShouldBe(ExecutionOutcome.Compensated);
        _batch.Resumes.ShouldBe(1);
        _batch.Active.ShouldBeTrue();
        _store.All.Single().History.Select(step => step.To).ShouldContain(ExecutionState.Compensating);
    }

    [Fact]
    public async Task A_compensation_that_cannot_undo_an_action_escalates()
    {
        _policy.Current = Autonomous();
        _batch.RefusePause = true;
        RemediationPlan plan = Commands.RollbackThenPause();

        await ExecuteAsync(Commands.Execute(_signer, plan));

        LastOutcome().ShouldBe(ExecutionOutcome.CompensationFailed);
        _outbox.Messages.OfType<RemediationExecuted>().Last().Detail.ShouldNotBeNull().ShouldContain("Responders must restore it");
    }

    [Fact]
    public async Task A_crash_during_compensation_resumes_compensation_on_reconciliation()
    {
        RemediationPlan plan = Commands.PausePlan();
        var request = ExecutionRequest.Receive("compensating", Commands.Tenant, Commands.Environment, IncidentId.New(), plan.PlanId, 1, plan.PlanHash, 7,
            System.Text.Json.JsonSerializer.Serialize(plan, Sentinel.Contracts.Messaging.SentinelJson.Options), Commands.Now);
        request.Reserve(AttestationId.New(), "responder@example.test", "policy-test", Commands.Now);
        request.ConfirmStart(Commands.Now);
        request.BeginExecuting(Commands.Now);
        request.ActionCompleted(plan.Actions[0].ActionId, Commands.Now);
        request.Fail("A later action failed.", Commands.Now);
        request.BeginCompensating(Commands.Now);
        await _store.SaveAsync(request, CancellationToken.None);
        _batch.Active = false;

        await Service().ReconcileAsync((await _store.UnfinishedAsync(Commands.Now, CancellationToken.None)).Single(), CancellationToken.None);

        LastOutcome().ShouldBe(ExecutionOutcome.Compensated);
        _batch.Resumes.ShouldBe(1);
    }

    private static PolicyDocument Autonomous() => FakePolicyStore.Reference() with
    {
        EnvironmentModes = new Dictionary<string, AutonomyMode> { ["reference"] = AutonomyMode.ApprovalRequired },
        Rules =
        [
            new PolicyRule { Id = "rollback", Action = "rollback", Environments = ["reference"], Grants = AutonomyMode.ApprovalRequired },
            new PolicyRule { Id = "pause", Action = "pause-batch", Environments = ["reference"], Grants = AutonomyMode.ApprovalRequired }
        ]
    };
}
