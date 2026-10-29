using CleanArchitecture.BuildingBlocks.Messaging;

namespace Sentinel.Executor.Application.BusinessTasks.Commands.Controls;

public sealed record SetEmergencyStopCommand(bool Engaged, string Actor) : ICommand;
