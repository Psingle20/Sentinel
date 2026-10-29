using CleanArchitecture.BuildingBlocks.Messaging;

namespace Sentinel.Executor.Application.BusinessTasks.Commands.Controls;

public sealed record SetActionDisabledCommand(string ActionKind, bool Disabled, string Actor) : ICommand;
