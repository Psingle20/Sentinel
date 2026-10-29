using CleanArchitecture.BuildingBlocks.Messaging;
using Reference.Scenarios.Application.BusinessProcesses.Scenarios;

namespace Reference.Scenarios.Application.BusinessProcesses.Scenarios;

/// <summary>Every scenario in the catalog, by ID.</summary>
public sealed record ListScenariosRequest : IRequest<IReadOnlyList<ScenarioSummary>>;
