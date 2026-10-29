using System.Globalization;

namespace Sentinel.Executor.Domain.Policy;

public enum PolicyVerdict
{
    Allow,
    Deny,
    ApprovalRequired,
    SuggestionOnly
}

/// <summary>An environment's autonomy mode: the most any rule may grant there (Phase 06).</summary>
public enum AutonomyMode
{
    ReadOnly,
    SuggestionOnly,
    ApprovalRequired,
    ConstrainedAutonomous
}

/// <summary>
/// One rule of the remediation policy. A rule matches an action class in some environments and on
/// some targets, and grants a mode when its conditions hold: bounded risk, enough decision
/// confidence, a small enough blast radius and, optionally, a severe enough incident.
/// </summary>
public sealed record PolicyRule
{
    public required string Id { get; init; }

    public required string Action { get; init; }

    /// <summary>Environments the rule covers; empty covers none, so a rule never applies everywhere by accident.</summary>
    public IReadOnlyList<string> Environments { get; init; } = [];

    /// <summary>Target services; empty means any target in the rule's environments.</summary>
    public IReadOnlyList<string> Targets { get; init; } = [];

    public string MaxRisk { get; init; } = "medium";

    /// <summary>Below this decision confidence the action is only suggested.</summary>
    public double MinConfidence { get; init; }

    public int MaxBlastRadius { get; init; } = 3;

    /// <summary>When set, less severe incidents get at most an approval-required verdict.</summary>
    public string? AutonomousFromSeverity { get; init; }

    public required AutonomyMode Grants { get; init; }
}

/// <summary>A change freeze: nothing in the listed environments runs while it lasts.</summary>
public sealed record ChangeFreeze(string Id, IReadOnlyList<string> Environments, DateTimeOffset From, DateTimeOffset Until, string Reason);

/// <summary>The versioned remediation policy (policy as code). Its version is recorded with every evaluation.</summary>
public sealed record PolicyDocument
{
    public required string Version { get; init; }

    public AutonomyMode DefaultMode { get; init; } = AutonomyMode.ApprovalRequired;

    /// <summary>Mode per <c>tenant/environment</c> or per environment, overriding <see cref="DefaultMode"/>.</summary>
    public IReadOnlyDictionary<string, AutonomyMode> EnvironmentModes { get; init; } = new Dictionary<string, AutonomyMode>(StringComparer.Ordinal);

    public IReadOnlyList<PolicyRule> Rules { get; init; } = [];

    public IReadOnlyList<ChangeFreeze> Freezes { get; init; } = [];
}

/// <summary>
/// Checks a policy document before it may be published (Phase 08 control plane): a document that
/// would silently change behaviour, such as a duplicate rule ID, an unknown risk or a freeze that
/// ends before it starts, is refused with every problem listed.
/// </summary>
public static class PolicyValidator
{
    private static readonly string[] Risks = ["low", "medium", "high"];
    private static readonly string[] Severities = ["low", "medium", "high", "critical"];

    public static IReadOnlyList<string> Problems(PolicyDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var problems = new List<string>();
        if (string.IsNullOrWhiteSpace(document.Version))
        {
            problems.Add("version is required.");
        }

        foreach (string duplicate in document.Rules.GroupBy(rule => rule.Id, StringComparer.Ordinal).Where(group => group.Count() > 1).Select(group => group.Key))
        {
            problems.Add($"rule ID '{duplicate}' is used more than once.");
        }

        foreach (PolicyRule rule in document.Rules)
        {
            if (string.IsNullOrWhiteSpace(rule.Id) || string.IsNullOrWhiteSpace(rule.Action))
            {
                problems.Add("every rule needs an id and an action.");
            }

            if (rule.Environments.Count == 0)
            {
                problems.Add($"rule '{rule.Id}' covers no environment.");
            }

            if (!Risks.Contains(rule.MaxRisk))
            {
                problems.Add($"rule '{rule.Id}' has unknown maxRisk '{rule.MaxRisk}'.");
            }

            if (rule.MinConfidence is < 0 or > 1)
            {
                problems.Add(string.Create(CultureInfo.InvariantCulture, $"rule '{rule.Id}' has minConfidence {rule.MinConfidence}; it must be between 0 and 1."));
            }

            if (rule.MaxBlastRadius < 1)
            {
                problems.Add($"rule '{rule.Id}' has maxBlastRadius below 1.");
            }

            if (rule.AutonomousFromSeverity is { } severity && !Severities.Contains(severity))
            {
                problems.Add($"rule '{rule.Id}' has unknown autonomousFromSeverity '{severity}'.");
            }
        }

        foreach (ChangeFreeze freeze in document.Freezes)
        {
            if (freeze.Until <= freeze.From)
            {
                problems.Add($"freeze '{freeze.Id}' ends before it starts.");
            }

            if (freeze.Environments.Count == 0)
            {
                problems.Add($"freeze '{freeze.Id}' covers no environment.");
            }
        }

        return problems;
    }
}

/// <summary>Operator controls independent of every model and workflow path (ADR-006).</summary>
public sealed record ExecutorControls(bool EmergencyStop, IReadOnlySet<string> DisabledActions)
{
    public static ExecutorControls None { get; } = new(false, new HashSet<string>(StringComparer.Ordinal));
}

/// <summary>What a policy decides on: the action and its scope, never free text from a model.</summary>
public sealed record PolicyInput(
    string TenantId,
    string EnvironmentId,
    string ActionKind,
    string TargetService,
    string Risk,
    double? Confidence,
    string? Severity,
    int BlastRadius,
    DateTimeOffset At);

public sealed record PolicyOutcome(PolicyVerdict Verdict, string PolicyVersion, IReadOnlyList<string> RuleTrace);

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
