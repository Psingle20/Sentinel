using System.Globalization;

namespace Sentinel.Executor.Domain.Policy;

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
