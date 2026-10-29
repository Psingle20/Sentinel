using CleanArchitecture.BuildingBlocks.Messaging;
using Reference.Scenarios.Application.Runs;

namespace Reference.Scenarios.Application.BusinessProcesses.Runs;

/// <summary>The current or last run; nothing before the first.</summary>
public sealed record GetCurrentRunRequest : IRequest<ScenarioRunView?>;
