using CleanArchitecture.BuildingBlocks.Messaging;
using Microsoft.AspNetCore.Mvc;
using Sentinel.Ingestion.Application.BusinessProcesses.Sources;

namespace Sentinel.Ingestion.Api.Controllers;

/// <summary>Provider health and watermarks, for operators.</summary>
[Route(Routes.Root + "/sources")]
public sealed class SourcesController(IRequestDispatcher requests) : ApiController
{
    [HttpGet]
    public async Task<IResult> List(CancellationToken cancellationToken) =>
        Match(await requests.HandleRequestAsync(new ListSourcesRequest(), cancellationToken));
}
