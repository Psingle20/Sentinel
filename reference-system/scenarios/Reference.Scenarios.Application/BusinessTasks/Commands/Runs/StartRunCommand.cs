using CleanArchitecture.BuildingBlocks.Messaging;
using Reference.Scenarios.Application.BusinessProcesses.Runs;

namespace Reference.Scenarios.Application.BusinessTasks.Commands.Runs;

public sealed record StartRunCommand(string ScenarioId) : ICommand<StartRunResult>;
