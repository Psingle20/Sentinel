using System.Text.Json;
using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Integration;

namespace Sentinel.Gateway.Application.Abstractions.Providers;

/// <summary>A control-plane call's answer, passed through from the owning service.</summary>
public sealed record ControlPlaneResponse(int StatusCode, JsonElement? Body, string? Problem)
{
    public bool IsSuccess => StatusCode is >= 200 and < 300;
}
