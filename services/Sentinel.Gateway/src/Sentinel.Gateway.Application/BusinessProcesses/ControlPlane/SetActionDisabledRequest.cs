using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.Gateway.Application.Abstractions.Providers;

namespace Sentinel.Gateway.Application.BusinessProcesses.ControlPlane;

/// <summary>Disables or enables one action kind at Executor, audited either way.</summary>
public sealed record SetActionDisabledRequest(string ActionKind, bool Disabled, string Actor) : IRequest<ControlPlaneResponse>;
