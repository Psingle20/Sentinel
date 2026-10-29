using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.Gateway.Application.Abstractions.Providers;
using System.Text.Json;

namespace Sentinel.Gateway.Application.BusinessTasks.Commands.ControlPlane;

public sealed record PublishConfigurationCommand(string Area, JsonElement Document, string ExpectedVersion, string Actor) : ICommand<ControlPlaneResponse>;
