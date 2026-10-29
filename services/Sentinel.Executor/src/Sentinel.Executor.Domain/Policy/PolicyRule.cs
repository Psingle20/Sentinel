using System.Globalization;

namespace Sentinel.Executor.Domain.Policy;

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
