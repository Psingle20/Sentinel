using System.Text.Json;
using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Integration;

namespace Sentinel.Gateway.Application.Abstractions.Providers;

/// <summary>
/// A control-plane call's answer, passed through from the owning service: its status, its body, and for a refusal its
/// description and the stable error code it gave, so a client can tell refusals apart without parsing their text.
/// </summary>
public sealed record ControlPlaneResponse(int StatusCode, JsonElement? Body, string? Problem, string? Code = null)
{
    public bool IsSuccess => StatusCode is >= 200 and < 300;
}
