using CleanArchitecture.BuildingBlocks;
using Sentinel.Gateway.Application.Abstractions.Providers;
using Sentinel.ServiceDefaults;

namespace Sentinel.Gateway.Api.Controllers;

/// <summary>Passes a control-plane answer through with its status code and problem.</summary>
internal static class ControlPlaneResult
{
    public static IResult From(Result<ControlPlaneResponse> result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return result.IsSuccess ? From(result.Value) : result.Error.ToProblem();
    }

    public static IResult From(ControlPlaneResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);

        if (response.IsSuccess)
        {
            return response.Body is { } body ? Results.Json(body, statusCode: response.StatusCode) : Results.StatusCode(response.StatusCode);
        }

        return Results.Problem(
            response.Problem,
            statusCode: response.StatusCode,
            title: response.Code,
            extensions: response.Code is { } code ? new Dictionary<string, object?> { ["code"] = code } : null);
    }
}
