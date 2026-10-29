using CleanArchitecture.BuildingBlocks.Messaging;
using Reference.Scenarios.Application.BusinessProcesses.Scenarios;

namespace Reference.Scenarios.Application.BusinessTasks.Queries.Scenarios;

public sealed record ListScenariosQuery : IQuery<IReadOnlyList<ScenarioSummary>>;
