using Microsoft.AspNetCore.Mvc;
using Sentinel.Executor.Application;

namespace Sentinel.Executor.Api.Controllers;

/// <summary>The execution audit trail.</summary>
[Route(Routes.Root + "/executions")]
public sealed class ExecutionsController(IExecutionStore store) : ApiController
{
    [HttpGet]
    public async Task<IResult> Recent(int? limit, CancellationToken cancellationToken) =>
        Results.Ok((await store.RecentAsync(Math.Clamp(limit ?? 50, 1, 200), cancellationToken)).Select(request => request.Snapshot()));
}