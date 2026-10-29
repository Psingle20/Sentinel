using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.Gateway.Application.Abstractions.Providers;

namespace Sentinel.Gateway.Application.BusinessProcesses.ControlPlane;

/// <summary>Executor's operator controls, as Executor answers.</summary>
public sealed record GetControlsRequest : IRequest<ControlPlaneResponse>;
