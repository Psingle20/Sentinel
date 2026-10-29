using CleanArchitecture.BuildingBlocks.Messaging;
using Reference.Scenarios.Domain.Scenarios;

namespace Reference.Scenarios.Application.BusinessTasks.Queries.Scenarios;

public sealed record GetScenarioQuery(string ScenarioId) : IQuery<ScenarioManifest?>;
