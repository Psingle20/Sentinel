using System.Text.Json;
using System.Text.Json.Serialization;
using CleanArchitecture.BuildingBlocks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.DependencyInjection;

namespace Sentinel.ServiceDefaults;

/// <summary>
/// HTTP conventions shared by every Sentinel API: the contract JSON rules (ADR-012) and problem
/// details for failed results, with stable error codes.
/// </summary>
public static class HttpConventions
{
    public static IServiceCollection AddSentinelJson(this IServiceCollection services) =>
        services.Configure<JsonOptions>(options => Apply(options.SerializerOptions));

    /// <summary>Applies the contract serialization rules to <paramref name="options"/>.</summary>
    public static void Apply(JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        options.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        options.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
    }

    /// <summary>A problem response for a failed result: 404, 409, 400 or 422 by error type, with the error code.</summary>
    public static IResult ToProblem(this Error error)
    {
        ArgumentNullException.ThrowIfNull(error);

        int status = error.Type switch
        {
            ErrorType.NotFound => StatusCodes.Status404NotFound,
            ErrorType.Conflict => StatusCodes.Status409Conflict,
            ErrorType.Validation => StatusCodes.Status400BadRequest,
            ErrorType.Problem => StatusCodes.Status422UnprocessableEntity,
            _ => StatusCodes.Status500InternalServerError
        };

        return Results.Problem(
            title: error.Code,
            detail: error.Description,
            statusCode: status,
            extensions: new Dictionary<string, object?> { ["code"] = error.Code });
    }

    public static IResult ToHttpResult<TValue>(this Result<TValue> result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblem();
    }
}
