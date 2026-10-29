using CleanArchitecture.BuildingBlocks.Messaging;
using Reference.Scenarios.Domain.Scenarios;

namespace Reference.Scenarios.Application.BusinessProcesses.Scenarios;

/// <summary>One scenario's manifest; nothing when the catalog has none.</summary>
public sealed record GetScenarioRequest(string ScenarioId) : IRequest<ScenarioManifest?>;
