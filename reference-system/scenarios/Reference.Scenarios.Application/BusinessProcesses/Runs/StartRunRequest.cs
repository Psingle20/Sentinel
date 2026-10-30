using CleanArchitecture.BuildingBlocks.Messaging;

namespace Reference.Scenarios.Application.BusinessProcesses.Runs;

/// <summary>Starts a scenario run from a clean reset, unless another run is still going.</summary>
public sealed record StartRunRequest(string ScenarioId, int? WarmupSeconds = null) : IRequest<StartRunResult>;
