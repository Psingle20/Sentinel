using CleanArchitecture.BuildingBlocks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sentinel.Contracts.Observations;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Runtime.Application;
using Sentinel.Runtime.Application.Incidents;
using Sentinel.Runtime.Application.Abstractions.Runtime;

namespace Sentinel.Runtime.Infrastructure.Grains;

/// <summary>A command's result in a form that crosses the grain boundary.</summary>
public sealed record CommandOutcome(CommandReply? Reply, string? ErrorCode, string? ErrorDescription, ErrorType ErrorType)
{
    public static CommandOutcome From(Result<CommandReply> result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return result.IsSuccess
            ? new CommandOutcome(result.Value, null, null, ErrorType.Failure)
            : new CommandOutcome(null, result.Error.Code, result.Error.Description, result.Error.Type);
    }

    public Result<CommandReply> ToResult() => Reply is { } reply
        ? Result.Success(reply)
        : Result.Failure<CommandReply>(ErrorType switch
        {
            ErrorType.NotFound => Error.NotFound(ErrorCode!, ErrorDescription!),
            ErrorType.Conflict => Error.Conflict(ErrorCode!, ErrorDescription!),
            ErrorType.Problem or ErrorType.Validation => Error.Problem(ErrorCode!, ErrorDescription!),
            _ => Error.Failure(ErrorCode!, ErrorDescription!)
        });
}
