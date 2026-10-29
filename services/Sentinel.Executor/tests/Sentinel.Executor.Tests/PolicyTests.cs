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
