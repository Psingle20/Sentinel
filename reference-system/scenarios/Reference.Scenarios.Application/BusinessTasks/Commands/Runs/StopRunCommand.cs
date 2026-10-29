using CleanArchitecture.BuildingBlocks.Messaging;

namespace Reference.Scenarios.Application.BusinessTasks.Commands.Runs;

public sealed record StopRunCommand(string RunId) : ICommand<bool>;
