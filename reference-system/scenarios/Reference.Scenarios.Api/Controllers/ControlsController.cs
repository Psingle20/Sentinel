using CleanArchitecture.BuildingBlocks.Messaging;
using Microsoft.AspNetCore.Mvc;
using Reference.Scenarios.Api.Control;
using Reference.Scenarios.Application.BusinessProcesses.Runs;

namespace Reference.Scenarios.Api.Controllers;

/// <summary>Resets the reference system and stops the batch, as the dashboard commands and end-to-end suites do.</summary>
[Route("api")]
public sealed class ControlsController(IRequestDispatcher requests) : ApiController
{
    [HttpPost("reset")]
    [ReferenceControl]
    public async Task<IResult> Reset(CancellationToken cancellationToken) =>
        MatchNoContent(await requests.HandleRequestAsync(new ResetReferenceRequest(), cancellationToken));

    [HttpPost("actions/stop-batch")]
    [ReferenceControl]
    public async Task<IResult> StopBatch(CancellationToken cancellationToken) =>
        MatchNoContent(await requests.HandleRequestAsync(new StopBatchRequest(), cancellationToken));
}
