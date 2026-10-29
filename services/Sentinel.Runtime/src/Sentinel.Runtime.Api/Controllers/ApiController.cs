using CleanArchitecture.BuildingBlocks;
using Microsoft.AspNetCore.Mvc;
using Sentinel.ServiceDefaults;

namespace Sentinel.Runtime.Api.Controllers;

/// <summary>
/// The base of Runtime's controllers (ADR-020): a <see cref="Result"/> becomes HTTP here and nowhere
/// else, as a problem document carrying the error's stable code on failure.
/// </summary>
[ApiController]
public abstract class ApiController : ControllerBase
{
    protected static IResult Match<TValue>(Result<TValue> result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return result.IsSuccess ? Results.Ok(result.Value) : Problem(result.Error);
    }

    protected static IResult MatchNoContent(Result result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return result.IsSuccess ? Results.NoContent() : Problem(result.Error);
    }

    protected static IResult Problem(Error error) => error.ToProblem();
}
