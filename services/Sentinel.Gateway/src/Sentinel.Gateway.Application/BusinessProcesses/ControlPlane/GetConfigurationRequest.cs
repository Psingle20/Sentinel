using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.Gateway.Application.Abstractions.Providers;

namespace Sentinel.Gateway.Application.BusinessProcesses.ControlPlane;

/// <summary>One area's control-plane document at the service that owns it.</summary>
public sealed record GetConfigurationRequest(string Area) : IRequest<ControlPlaneResponse>;
