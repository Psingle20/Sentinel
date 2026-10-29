using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.Gateway.Application.Abstractions.Providers;

namespace Sentinel.Gateway.Application.BusinessProcesses.ControlPlane;

/// <summary>Every published version of Executor's policy, as Executor answers.</summary>
public sealed record GetPolicyHistoryRequest : IRequest<ControlPlaneResponse>;
