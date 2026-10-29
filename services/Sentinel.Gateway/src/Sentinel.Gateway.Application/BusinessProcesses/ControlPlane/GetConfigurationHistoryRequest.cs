using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.Gateway.Application.Abstractions.Providers;

namespace Sentinel.Gateway.Application.BusinessProcesses.ControlPlane;

/// <summary>Every published version of one area's document at the service that owns it.</summary>
public sealed record GetConfigurationHistoryRequest(string Area) : IRequest<ControlPlaneResponse>;
