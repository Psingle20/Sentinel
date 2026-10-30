using CleanArchitecture.BuildingBlocks;
using Microsoft.AspNetCore.Mvc;
using Sentinel.ServiceDefaults;

namespace Sentinel.AgentWorker.Api.Controllers;

/// <summary>
/// The base of AgentWorker's controllers (ADR-020): a <see cref="Result"/> becomes HTTP here and nowhere
/// else. Nothing found is a bare 404, which the status-code pages answer, as it was before controllers.
/// </summary>
[ApiController]
public abstract class ApiController : ControllerBase
{
    protected static IResult Match<TValue>(Result<TValue> result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return result.IsSuccess ? Results.Ok(result.Value) : Problem(result.Error);
    }

    protected static IResult MatchFound<TValue>(Result<TValue?> result)
        where TValue : class
    {
        ArgumentNullException.ThrowIfNull(result);

        if (result.IsFailure)
        {
            return Problem(result.Error);
        }

        return result.Value is { } value ? Results.Ok(value) : Results.NotFound();
    }

    /// <summary>
    /// As <see cref="MatchFound{TValue}"/>, except that a request the replay refuses is a bare 400 whose
    /// detail says why, the answer the replay endpoint has always given.
    /// </summary>
    protected static IResult MatchReplay<TValue>(Result<TValue?> result)
        where TValue : class
    {
        ArgumentNullException.ThrowIfNull(result);

        return result.IsFailure && result.Error.Type == ErrorType.Validation
            ? Results.Problem(result.Error.Description, statusCode: StatusCodes.Status400BadRequest)
            : MatchFound(result);
    }

    protected static IResult Problem(Error error) => error.ToProblem();
}
