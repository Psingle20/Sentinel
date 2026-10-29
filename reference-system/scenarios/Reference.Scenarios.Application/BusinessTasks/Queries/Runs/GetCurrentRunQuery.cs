using CleanArchitecture.BuildingBlocks.Messaging;
using Reference.Scenarios.Application.Runs;

namespace Reference.Scenarios.Application.BusinessTasks.Queries.Runs;

public sealed record GetCurrentRunQuery : IQuery<ScenarioRunView?>;
