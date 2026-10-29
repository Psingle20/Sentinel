using System.Globalization;

namespace Reference.Scenarios.Application.Runs;

/// <summary>
/// A run's progress. <see cref="ActivatedAt"/> is the time of the first step: every offset in the
/// manifest is relative to it.
/// </summary>
public sealed record ScenarioRunView(
    string RunId,
    string ScenarioId,
    string Phase,
    DateTimeOffset RequestedAt,
    DateTimeOffset? ActivatedAt,
    DateTimeOffset? CompletedAt,
    IReadOnlyList<StepExecution> Steps,
    string? Error);
