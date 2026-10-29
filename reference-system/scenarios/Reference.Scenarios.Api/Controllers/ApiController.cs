using CleanArchitecture.BuildingBlocks;
using Microsoft.AspNetCore.Mvc;

namespace Reference.Scenarios.Api.Controllers;

/// <summary>The base of the scenario runner's controllers: a <see cref="Result"/> becomes HTTP here. Nothing found is a bare 404.</summary>
[ApiController]
public abstract class ApiController : ControllerBase
{
    protected static IResult Match<TValue>(Result<TValue> result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return result.IsSuccess ? Results.Ok(result.Value) : Results.Problem(result.Error.Description, title: result.Error.Code);
    }

    protected static IResult MatchFound<TValue>(Result<TValue?> result)
        where TValue : class
    {
        ArgumentNullException.ThrowIfNull(result);

        if (result.IsFailure)
        {
            return Results.Problem(result.Error.Description, title: result.Error.Code);
        }

        return result.Value is { } value ? Results.Ok(value) : Results.NotFound();
    }

    protected static IResult MatchNoContent(Result result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return result.IsSuccess ? Results.NoContent() : Results.Problem(result.Error.Description, title: result.Error.Code);
    }
}
