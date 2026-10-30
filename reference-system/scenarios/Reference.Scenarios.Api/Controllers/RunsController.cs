using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Messaging;
using Microsoft.AspNetCore.Mvc;
using Reference.Scenarios.Api.Contracts;
using Reference.Scenarios.Api.Control;
using Reference.Scenarios.Application.BusinessProcesses.Runs;
using Reference.Scenarios.Application.Runs;

namespace Reference.Scenarios.Api.Controllers;

/// <summary>Scenario runs, one at a time: started from a clean reset, stopped between steps.</summary>
[Route("api/runs")]
public sealed class RunsController(IRequestDispatcher requests) : ApiController
{
    [HttpGet("current")]
    public async Task<IResult> Current(CancellationToken cancellationToken)
    {
        Result<ScenarioRunView?> current = await requests.HandleRequestAsync(new GetCurrentRunRequest(), cancellationToken);
        return current.IsSuccess && current.Value is null ? Results.NoContent() : MatchFound(current);
    }

    [HttpGet("{runId}")]
    public async Task<IResult> Get(string runId, CancellationToken cancellationToken) =>
        MatchFound(await requests.HandleRequestAsync(new GetRunRequest(runId), cancellationToken));

    [HttpPost]
    [ReferenceControl]
    public async Task<IResult> Start(StartRunBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);

        Result<StartRunResult> started = await requests.HandleRequestAsync(new StartRunRequest(body.ScenarioId), cancellationToken);
        if (started.IsFailure)
        {
            return Results.Problem(started.Error.Description, title: started.Error.Code);
        }

        return started.Value switch
        {
            { Outcome: StartRunOutcome.Started, Run: { } run } => Results.Accepted($"/api/runs/{run.RunId}", run),
            { Outcome: StartRunOutcome.RunActive, Run: var active } => Results.Conflict(active),
            { Outcome: StartRunOutcome.WrongEnvironment } => Results.Problem(
                $"{body.ScenarioId} belongs to another reference environment than the one this runner drives.",
                statusCode: StatusCodes.Status409Conflict,
                title: "Runs.WrongEnvironment"),
            _ => Results.NotFound()
        };
    }

    [HttpPost("{runId}/stop")]
    [ReferenceControl]
    public async Task<IResult> Stop(string runId, CancellationToken cancellationToken)
    {
        Result<bool> stopped = await requests.HandleRequestAsync(new StopRunRequest(runId), cancellationToken);
        if (stopped.IsFailure)
        {
            return Results.Problem(stopped.Error.Description, title: stopped.Error.Code);
        }

        return stopped.Value ? Results.NoContent() : Results.NotFound();
    }
}
