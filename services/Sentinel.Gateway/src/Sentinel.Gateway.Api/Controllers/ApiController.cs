using CleanArchitecture.BuildingBlocks;
using Microsoft.AspNetCore.Mvc;
using Sentinel.ServiceDefaults;

namespace Sentinel.Gateway.Api.Controllers;

/// <summary>
/// The base of the Gateway's controllers (ADR-020): a <see cref="Result"/> becomes HTTP here and
/// nowhere else. A failure is a problem document carrying the error's stable code, with the status
/// its type implies; actions do not inspect results themselves.
/// </summary>
[ApiController]
public abstract class ApiController : ControllerBase
{
    protected static IResult Match<TValue>(Result<TValue> result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return result.IsSuccess ? Results.Ok(result.Value) : Problem(result.Error);
    }

    protected static IResult MatchAccepted(Result result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return result.IsSuccess ? Results.Accepted() : Problem(result.Error);
    }

    protected static IResult MatchAccepted<TValue>(Result<TValue> result, string location)
    {
        ArgumentNullException.ThrowIfNull(result);

        return result.IsSuccess ? Results.Accepted(location, result.Value) : Problem(result.Error);
    }

    protected static IResult Problem(Error error) => error.ToProblem();
}
