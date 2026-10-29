using CleanArchitecture.BuildingBlocks.Messaging;
using Reference.Scenarios.Application.Runs;

namespace Reference.Scenarios.Application.BusinessProcesses.Runs;

/// <summary>One run by ID; nothing when it is unknown.</summary>
public sealed record GetRunRequest(string RunId) : IRequest<ScenarioRunView?>;
