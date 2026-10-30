namespace Reference.Scenarios.Api.Contracts;

/// <summary>
/// The scenario to run and, optionally, a shorter warm-up than its manifest's, for a caller that has just seen the system
/// healthy for longer than that, as the end-to-end suites' quiet check does. A warm-up is never lengthened.
/// </summary>
public sealed record StartRunBody(string ScenarioId, int? WarmupSeconds = null);
