using CleanArchitecture.BuildingBlocks.Messaging;
using Microsoft.AspNetCore.Mvc;
using Sentinel.Executor.Application.BusinessProcesses.Executions;

namespace Sentinel.Executor.Api.Controllers;

/// <summary>The execution audit trail.</summary>
[Route(Routes.Root + "/executions")]
public sealed class ExecutionsController(IRequestDispatcher requests) : ApiController
{
    [HttpGet]
    public async Task<IResult> Recent(int? limit, CancellationToken cancellationToken) =>
        Match(await requests.HandleRequestAsync(new ListExecutionsRequest(limit ?? 50), cancellationToken));
}
