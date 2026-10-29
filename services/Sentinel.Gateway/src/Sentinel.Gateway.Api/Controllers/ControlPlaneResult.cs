using Sentinel.Gateway.Application.ControlPlane;

namespace Sentinel.Gateway.Api;

/// <summary>Passes a control-plane answer through with its status code and problem.</summary>
internal static class ControlPlaneResult
{
    public static IResult From(ControlPlaneResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);

        if (response.IsSuccess)
        {
            return response.Body is { } body ? Results.Json(body, statusCode: response.StatusCode) : Results.StatusCode(response.StatusCode);
        }

        return Results.Problem(response.Problem, statusCode: response.StatusCode);
    }
}