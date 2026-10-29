using CleanArchitecture.BuildingBlocks.Messaging;

namespace Reference.Scenarios.Application.BusinessProcesses.Runs;

/// <summary>Stops a run between steps; already-executed steps are not undone.</summary>
public sealed record StopRunRequest(string RunId) : IRequest<bool>;
