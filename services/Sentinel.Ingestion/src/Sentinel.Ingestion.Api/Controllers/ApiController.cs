using CleanArchitecture.BuildingBlocks;
using Microsoft.AspNetCore.Mvc;
using Sentinel.Ingestion.Application.BusinessTasks.Queries;
using Sentinel.ServiceDefaults;

namespace Sentinel.Ingestion.Api.Controllers;

/// <summary>
/// The base of Ingestion's controllers (ADR-020): a <see cref="Result"/> becomes HTTP here and nowhere
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

    /// <summary>
    /// As <see cref="Match{TValue}"/>, except that a full query bulkhead is 503, so the caller retries
    /// shortly instead of treating the refusal as a fault (Phase 02).
    /// </summary>
    protected static IResult MatchQuery<TValue>(Result<TValue> result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return result.IsFailure && result.Error == QueryErrors.BulkheadFull
            ? Results.Problem(
                title: result.Error.Code,
                detail: result.Error.Description,
                statusCode: StatusCodes.Status503ServiceUnavailable,
                extensions: new Dictionary<string, object?> { ["code"] = result.Error.Code })
            : Match(result);
    }

    protected static IResult Problem(Error error) => error.ToProblem();
}
