using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.Gateway.Application.Abstractions.Providers;

namespace Sentinel.Gateway.Application.BusinessProcesses.ControlPlane;

/// <summary>Engages or releases Executor's emergency stop, audited either way.</summary>
public sealed record SetEmergencyStopRequest(bool Engaged, string Actor) : IRequest<ControlPlaneResponse>;
