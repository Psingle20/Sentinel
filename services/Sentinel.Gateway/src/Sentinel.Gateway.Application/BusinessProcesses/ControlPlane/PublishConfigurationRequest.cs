using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.Gateway.Application.Abstractions.Providers;
using System.Text.Json;

namespace Sentinel.Gateway.Application.BusinessProcesses.ControlPlane;

/// <summary>A new version of one area's document for its owner to validate and publish, audited either way.</summary>
public sealed record PublishConfigurationRequest(string Area, JsonElement Document, string ExpectedVersion, string Actor) : IRequest<ControlPlaneResponse>;
