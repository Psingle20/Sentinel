using Reference.Scenarios.Application.Runs;

namespace Reference.Scenarios.Application.BusinessProcesses.Runs;

/// <summary>How a start request ended, with the run it started or the run still going.</summary>
public sealed record StartRunResult(StartRunOutcome Outcome, ScenarioRunView? Run);
