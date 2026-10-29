using System.Globalization;

namespace Sentinel.Executor.Domain.Policy;

/// <summary>
/// Evaluates the policy document (Phase 06): emergency stop, disabled actions and change freezes
/// first, then the first rule matching the action, its conditions, and finally the environment's
/// mode as a ceiling. No matching rule denies. Deterministic: the same inputs and document give the
/// same verdict and trace, so plan-time and execution-time evaluations agree unless something changed.
/// </summary>
public static class PolicyEngine
{
    private static readonly string[] RiskOrder = ["low", "medium", "high"];
    private static readonly string[] SeverityOrder = ["low", "medium", "high", "critical"];

    public static PolicyOutcome Evaluate(PolicyDocument policy, PolicyInput input, ExecutorControls controls)
    {
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(controls);

        if (controls.EmergencyStop)
        {
            return new PolicyOutcome(PolicyVerdict.Deny, policy.Version, ["executor.emergency-stop: every action is denied"]);
        }

        if (controls.DisabledActions.Contains(input.ActionKind))
        {
            return new PolicyOutcome(PolicyVerdict.Deny, policy.Version, [$"executor.disabled: {input.ActionKind} actions are disabled by an operator"]);
        }

        if (policy.Freezes.FirstOrDefault(freeze => freeze.Environments.Contains(input.EnvironmentId) && input.At >= freeze.From && input.At < freeze.Until) is { } frozen)
        {
            return new PolicyOutcome(PolicyVerdict.Deny, policy.Version, [$"freeze.{frozen.Id}: {frozen.Reason} until {frozen.Until:O}"]);
        }

        var trace = new List<string>();
        PolicyRule? rule = policy.Rules.FirstOrDefault(candidate =>
            candidate.Action == input.ActionKind
            && candidate.Environments.Contains(input.EnvironmentId)
            && (candidate.Targets.Count == 0 || candidate.Targets.Contains(input.TargetService)));
        if (rule is null)
        {
            return new PolicyOutcome(PolicyVerdict.Deny, policy.Version, [$"action.{input.ActionKind}: no rule permits it on {input.TargetService} in {input.EnvironmentId}", "default: deny"]);
        }

        if (Rank(RiskOrder, input.Risk) > Rank(RiskOrder, rule.MaxRisk))
        {
            return new PolicyOutcome(PolicyVerdict.Deny, policy.Version, [$"{rule.Id}.risk: {input.Risk} exceeds the rule's {rule.MaxRisk}"]);
        }

        if (input.BlastRadius > rule.MaxBlastRadius)
        {
            return new PolicyOutcome(PolicyVerdict.Deny, policy.Version, [string.Create(CultureInfo.InvariantCulture, $"{rule.Id}.blast-radius: {input.BlastRadius} services exceed the rule's {rule.MaxBlastRadius}")]);
        }

        PolicyVerdict verdict = Verdict(rule.Grants);
        trace.Add($"{rule.Id}: {input.ActionKind} on {input.TargetService} in {input.EnvironmentId} grants {rule.Grants}");

        if (input.Confidence is { } confidence && confidence < rule.MinConfidence)
        {
            verdict = MostRestrictive(verdict, PolicyVerdict.SuggestionOnly);
            trace.Add(string.Create(CultureInfo.InvariantCulture, $"{rule.Id}.confidence: {confidence:F2} is below {rule.MinConfidence:F2}, so the action is only suggested"));
        }

        if (verdict == PolicyVerdict.Allow && rule.AutonomousFromSeverity is { } floor && Rank(SeverityOrder, input.Severity ?? "low") < Rank(SeverityOrder, floor))
        {
            verdict = PolicyVerdict.ApprovalRequired;
            trace.Add($"{rule.Id}.severity: autonomous only from {floor}; {input.Severity ?? "unknown"} needs approval");
        }

        AutonomyMode mode = ModeFor(policy, input);
        PolicyVerdict ceiling = Verdict(mode);
        if (Restrictiveness(ceiling) > Restrictiveness(verdict))
        {
            trace.Add($"mode.{input.EnvironmentId}: {mode} allows at most {ceiling}");
            verdict = ceiling;
        }

        return new PolicyOutcome(verdict, policy.Version, trace);
    }

    public static PolicyVerdict MostRestrictive(PolicyVerdict first, PolicyVerdict second) =>
        Restrictiveness(first) >= Restrictiveness(second) ? first : second;

    public static int Restrictiveness(PolicyVerdict verdict) => verdict switch
    {
        PolicyVerdict.Allow => 0,
        PolicyVerdict.ApprovalRequired => 1,
        PolicyVerdict.SuggestionOnly => 2,
        _ => 3
    };

    private static AutonomyMode ModeFor(PolicyDocument policy, PolicyInput input)
    {
        if (policy.EnvironmentModes.TryGetValue($"{input.TenantId}/{input.EnvironmentId}", out AutonomyMode scoped))
        {
            return scoped;
        }

        return policy.EnvironmentModes.TryGetValue(input.EnvironmentId, out AutonomyMode environment) ? environment : policy.DefaultMode;
    }

    private static PolicyVerdict Verdict(AutonomyMode mode) => mode switch
    {
        AutonomyMode.ConstrainedAutonomous => PolicyVerdict.Allow,
        AutonomyMode.ApprovalRequired => PolicyVerdict.ApprovalRequired,
        AutonomyMode.SuggestionOnly => PolicyVerdict.SuggestionOnly,
        _ => PolicyVerdict.Deny
    };

    private static int Rank(string[] order, string value) =>
        Array.IndexOf(order, value.ToLowerInvariant()) is var index and >= 0 ? index : order.Length;
}
