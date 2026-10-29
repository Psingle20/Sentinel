using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.Gateway.Application.Abstractions.Providers;

namespace Sentinel.Gateway.Application.BusinessTasks.Commands.ControlPlane;

public sealed record SetEmergencyStopCommand(bool Engaged, string Actor) : ICommand<ControlPlaneResponse>;
