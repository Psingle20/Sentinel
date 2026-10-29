using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.Gateway.Application.Abstractions.Providers;

namespace Sentinel.Gateway.Application.BusinessProcesses.ControlPlane;

/// <summary>Executor's current policy document, as Executor answers.</summary>
public sealed record GetPolicyRequest : IRequest<ControlPlaneResponse>;
