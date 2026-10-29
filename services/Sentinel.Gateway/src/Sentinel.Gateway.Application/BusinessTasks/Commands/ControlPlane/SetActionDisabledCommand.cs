using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.Gateway.Application.Abstractions.Providers;

namespace Sentinel.Gateway.Application.BusinessTasks.Commands.ControlPlane;

public sealed record SetActionDisabledCommand(string ActionKind, bool Disabled, string Actor) : ICommand<ControlPlaneResponse>;
