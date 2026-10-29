using System.Security.Cryptography;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Contracts.Remediation;
using Sentinel.Executor.Application;
using Sentinel.Executor.Domain.Executions;
using Sentinel.Executor.Domain.Policy;
using Shouldly;
using MicrosoftOptions = Microsoft.Extensions.Options.Options;

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

    private ExecutionOutcome LastOutcome() => _outbox.Messages.OfType<RemediationExecuted>().Last().Outcome;

    [Fact]
    public async Task An_approved_plan_is_confirmed_executed_once_and_verified()
    {
        await Service().HandleAsync(Commands.Execute(_signer), CancellationToken.None);

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

        await Service().HandleAsync(command, CancellationToken.None);
        await Service().HandleAsync(command, CancellationToken.None);

        _target.Executions.ShouldBe(1);
        _outbox.Messages.OfType<RemediationExecuted>().Count().ShouldBe(2);
    }

    [Fact]
    public async Task An_attestation_cannot_be_used_twice()
    {
        ExecuteRemediation first = Commands.Execute(_signer);
        ExecuteRemediation replay = first with { IdempotencyKey = first.IdempotencyKey + ":replay", FencingToken = first.FencingToken + 1 };

        await Service().HandleAsync(first, CancellationToken.None);
        await Service().HandleAsync(replay, CancellationToken.None);

        _target.Executions.ShouldBe(1);
        LastOutcome().ShouldBe(ExecutionOutcome.Rejected);
    }

    [Fact]
    public async Task A_fencing_token_lower_than_one_already_seen_is_rejected()
    {
        var incident = IncidentId.New();
        await Service().HandleAsync(Commands.Execute(_signer, fencingToken: 9, key: "k1", incident: incident), CancellationToken.None);
        _target.Serving = "v2.4.1";
        _target.Operation = OperationStatus.NotFound;

        await Service().HandleAsync(Commands.Execute(_signer, fencingToken: 8, key: "k2", incident: incident), CancellationToken.None);

        LastOutcome().ShouldBe(ExecutionOutcome.Rejected);
        _target.Executions.ShouldBe(1);
    }

    [Fact]
    public async Task Without_Runtime_confirmation_nothing_runs()
    {
        _start.Confirm = false;
        await Service().HandleAsync(Commands.Execute(_signer, key: "refused"), CancellationToken.None);
        LastOutcome().ShouldBe(ExecutionOutcome.Aborted);

        _start.Confirm = true;
        _start.Unreachable = true;
        await Service().HandleAsync(Commands.Execute(_signer, key: "unreachable"), CancellationToken.None);
        LastOutcome().ShouldBe(ExecutionOutcome.Aborted);

        _target.Executions.ShouldBe(0);
    }

    [Fact]
    public async Task A_target_not_serving_the_plans_from_revision_fails_the_precondition()
    {
        _target.Serving = "v2.4.3";

        await Service().HandleAsync(Commands.Execute(_signer), CancellationToken.None);

        LastOutcome().ShouldBe(ExecutionOutcome.Rejected);
        _start.Calls.ShouldBe(0);
    }

    [Fact]
    public async Task A_denied_plan_is_rejected_even_with_a_valid_attestation()
    {
        RemediationPlan plan = Commands.Plan(target: "payments-service");

        await Service().HandleAsync(Commands.Execute(_signer, plan), CancellationToken.None);

        LastOutcome().ShouldBe(ExecutionOutcome.Rejected);
        _target.Executions.ShouldBe(0);
    }

    [Fact]
    public async Task An_ambiguous_result_becomes_outcome_unknown_and_is_reconciled_from_the_target_without_repeating_the_action()
    {
        _target.NextOutcome = AdapterOutcome.Unknown;
        await Service().HandleAsync(Commands.Execute(_signer), CancellationToken.None);
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
    public async Task Every_action_runs_under_its_own_single_use_capability()
    {
        await Service().HandleAsync(Commands.Execute(_signer), CancellationToken.None);

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

        await Service().HandleAsync(Commands.Execute(_signer, plan), CancellationToken.None);

        LastOutcome().ShouldBe(ExecutionOutcome.Rejected);
        _outbox.Messages.OfType<RemediationExecuted>().Last().Detail.ShouldNotBeNull().ShouldContain("SuggestionOnly");
        _batch.Active.ShouldBeTrue();
    }

    [Fact]
    public async Task The_emergency_stop_engaged_at_run_time_rejects_execution()
    {
        await _controls.SetEmergencyStopAsync(true, "operator@example.test", CancellationToken.None);

        await Service().HandleAsync(Commands.Execute(_signer), CancellationToken.None);

        LastOutcome().ShouldBe(ExecutionOutcome.Rejected);
        _target.Executions.ShouldBe(0);
    }

    [Fact]
    public async Task A_plan_that_fails_part_way_is_compensated_in_reverse()
    {
        _policy.Current = Autonomous();
        _target.NextOutcome = AdapterOutcome.Rejected;
        RemediationPlan plan = Commands.PausePlan(alsoRollback: true);

        await Service().HandleAsync(Commands.Execute(_signer, plan), CancellationToken.None);

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

        await Service().HandleAsync(Commands.Execute(_signer, plan), CancellationToken.None);

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

public sealed class AttestationTests : IDisposable
{
    private readonly TestSigner _signer = new();
    private readonly FakeTimeProvider _time = new(Commands.Now);

    public void Dispose() => _signer.Dispose();

    private Task<CleanArchitecture.BuildingBlocks.Result<ApprovalAttestationClaims>> Verify(ExecuteRemediation command) =>
        new AttestationVerifier(_signer, _time).VerifyAsync(command.Attestation, command, CancellationToken.None);

    [Fact]
    public async Task A_valid_attestation_is_accepted()
    {
        (await Verify(Commands.Execute(_signer))).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task A_forged_signature_is_refused()
    {
        using var attacker = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        ExecuteRemediation command = Commands.Execute(_signer);
        ApprovalAttestationClaims claims = Commands.Claims(command.IncidentId, command.Plan);

        (await Verify(command with { Attestation = _signer.Sign(claims, key: attacker) })).Error.Description.ShouldContain("signature");
    }

    [Theory]
    [InlineData("audience")]
    [InlineData("expired")]
    [InlineData("hash")]
    [InlineData("incident")]
    [InlineData("scope")]
    public async Task Claims_that_do_not_bind_to_this_execution_are_refused(string problem)
    {
        ExecuteRemediation command = Commands.Execute(_signer, claims: claims => problem switch
        {
            "audience" => claims with { Audience = "sentinel-runtime" },
            "expired" => claims with { ExpiresAt = Commands.Now.AddMinutes(-5) },
            "hash" => claims with { PlanHash = "sha256:other" },
            "incident" => claims with { IncidentId = IncidentId.New() },
            _ => claims with { ApprovalScope = "read" }
        });

        (await Verify(command)).IsFailure.ShouldBeTrue();
    }

    [Fact]
    public async Task An_unknown_key_and_a_malformed_token_are_refused()
    {
        ExecuteRemediation command = Commands.Execute(_signer);
        ApprovalAttestationClaims claims = Commands.Claims(command.IncidentId, command.Plan);

        (await Verify(command with { Attestation = _signer.Sign(claims, keyId: "rotated-away") })).Error.Description.ShouldContain("not a published");
        (await Verify(command with { Attestation = "not-a-jws" })).IsFailure.ShouldBeTrue();
        (await Verify(command with { Attestation = string.Empty })).IsFailure.ShouldBeTrue();
    }
}

public sealed class PolicyTests
{
    private static readonly DateTimeOffset At = Commands.Now;

    [Theory]
    [InlineData("rollback", "reference", "checkout-service", "medium", 0.8, 2, PolicyVerdict.ApprovalRequired)]
    [InlineData("rollback", "reference", "order-service", "medium", 0.8, 1, PolicyVerdict.ApprovalRequired)]
    [InlineData("rollback", "production", "checkout-service", "medium", 0.8, 2, PolicyVerdict.Deny)]
    [InlineData("rollback", "reference", "payments-service", "medium", 0.8, 2, PolicyVerdict.Deny)]
    [InlineData("restart", "reference", "checkout-service", "medium", 0.8, 1, PolicyVerdict.Deny)]
    [InlineData("rollback", "reference", "checkout-service", "high", 0.8, 2, PolicyVerdict.Deny)]
    [InlineData("rollback", "reference", "checkout-service", "medium", 0.8, 3, PolicyVerdict.Deny)]
    [InlineData("rollback", "reference", "checkout-service", "medium", 0.49, 2, PolicyVerdict.SuggestionOnly)]
    [InlineData("rollback", "reference", "checkout-service", "medium", 0.5, 2, PolicyVerdict.ApprovalRequired)]
    [InlineData("pause-batch", "reference", "reporting-worker", "low", 0.9, 1, PolicyVerdict.SuggestionOnly)]
    public void The_reference_policy_decides_by_action_scope_risk_confidence_and_blast_radius(
        string action, string environment, string target, string risk, double confidence, int blastRadius, PolicyVerdict expected)
    {
        PolicyOutcome outcome = PolicyEngine.Evaluate(FakePolicyStore.Reference(), Input(action, environment, target, risk, confidence, blastRadius), ExecutorControls.None);

        outcome.Verdict.ShouldBe(expected);
        outcome.RuleTrace.ShouldNotBeEmpty();
        outcome.PolicyVersion.ShouldBe("policy-test");
    }

    [Fact]
    public void The_emergency_stop_and_a_disabled_action_deny_whatever_the_rules_say()
    {
        PolicyInput input = Input("rollback", "reference", "checkout-service", "medium", 0.9, 1);

        PolicyEngine.Evaluate(FakePolicyStore.Reference(), input, new ExecutorControls(true, new HashSet<string>())).Verdict.ShouldBe(PolicyVerdict.Deny);
        PolicyEngine.Evaluate(FakePolicyStore.Reference(), input, new ExecutorControls(false, new HashSet<string> { "rollback" })).RuleTrace.Single().ShouldContain("disabled");
    }

    [Fact]
    public void A_change_freeze_denies_inside_its_window_only()
    {
        PolicyDocument frozen = FakePolicyStore.Reference() with { Freezes = [new ChangeFreeze("release", ["reference"], At.AddMinutes(-5), At.AddMinutes(5), "Release freeze")] };

        PolicyEngine.Evaluate(frozen, Input("rollback", "reference", "checkout-service", "medium", 0.9, 1), ExecutorControls.None).Verdict.ShouldBe(PolicyVerdict.Deny);
        PolicyEngine.Evaluate(frozen, Input("rollback", "reference", "checkout-service", "medium", 0.9, 1) with { At = At.AddMinutes(10) }, ExecutorControls.None).Verdict.ShouldBe(PolicyVerdict.ApprovalRequired);
    }

    [Theory]
    [InlineData(AutonomyMode.ReadOnly, PolicyVerdict.Deny)]
    [InlineData(AutonomyMode.SuggestionOnly, PolicyVerdict.SuggestionOnly)]
    [InlineData(AutonomyMode.ApprovalRequired, PolicyVerdict.ApprovalRequired)]
    [InlineData(AutonomyMode.ConstrainedAutonomous, PolicyVerdict.Allow)]
    public void The_environment_mode_caps_what_a_rule_grants(AutonomyMode mode, PolicyVerdict expected)
    {
        PolicyDocument policy = FakePolicyStore.Reference() with
        {
            EnvironmentModes = new Dictionary<string, AutonomyMode> { ["reference"] = mode },
            Rules = [new PolicyRule { Id = "autonomous", Action = "rollback", Environments = ["reference"], Grants = AutonomyMode.ConstrainedAutonomous }]
        };

        PolicyEngine.Evaluate(policy, Input("rollback", "reference", "checkout-service", "low", 0.9, 1), ExecutorControls.None).Verdict.ShouldBe(expected);
    }

    [Fact]
    public void Autonomy_can_be_limited_to_severe_incidents()
    {
        PolicyDocument policy = FakePolicyStore.Reference() with
        {
            EnvironmentModes = new Dictionary<string, AutonomyMode> { ["reference"] = AutonomyMode.ConstrainedAutonomous },
            Rules = [new PolicyRule { Id = "autonomous", Action = "rollback", Environments = ["reference"], AutonomousFromSeverity = "critical", Grants = AutonomyMode.ConstrainedAutonomous }]
        };

        PolicyEngine.Evaluate(policy, Input("rollback", "reference", "checkout-service", "low", 0.9, 1) with { Severity = "critical" }, ExecutorControls.None).Verdict.ShouldBe(PolicyVerdict.Allow);
        PolicyEngine.Evaluate(policy, Input("rollback", "reference", "checkout-service", "low", 0.9, 1) with { Severity = "medium" }, ExecutorControls.None).Verdict.ShouldBe(PolicyVerdict.ApprovalRequired);
    }

    private static PolicyInput Input(string action, string environment, string target, string risk, double confidence, int blastRadius) =>
        new("acme", environment, action, target, risk, confidence, "high", blastRadius, At);
    [Fact]
    public void An_execution_request_moves_only_along_the_state_machine()
    {
        var request = ExecutionRequest.Receive("k", Commands.Tenant, Commands.Environment, IncidentId.New(), PlanId.New(), 1, "h", 1, "{}", Commands.Now);

        request.BeginExecuting(Commands.Now).IsFailure.ShouldBeTrue();
        request.Reject("no", Commands.Now).IsSuccess.ShouldBeTrue();
        request.Reserve(AttestationId.New(), "a", "p", Commands.Now).IsFailure.ShouldBeTrue();
        request.HasFinalOutcome.ShouldBeTrue();
    }
}
