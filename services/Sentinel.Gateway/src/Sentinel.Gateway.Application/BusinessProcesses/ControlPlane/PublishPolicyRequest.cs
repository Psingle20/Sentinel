using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.Gateway.Application.Abstractions.Providers;
using System.Text.Json;

namespace Sentinel.Gateway.Application.BusinessProcesses.ControlPlane;

/// <summary>A new policy version for Executor to validate and publish, audited either way.</summary>
public sealed record PublishPolicyRequest(JsonElement Document, string ExpectedVersion, string Actor) : IRequest<ControlPlaneResponse>;
