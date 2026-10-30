using CleanArchitecture.BuildingBlocks.Messaging;
using Microsoft.AspNetCore.Mvc;
using Reference.Scenarios.Api.Control;
using Reference.Scenarios.Application.BusinessProcesses.Environments;

namespace Reference.Scenarios.Api.Controllers;

/// <summary>The reference environment the runner drives: whether it is ready, and provisioning it ahead of a run.</summary>
[Route("api/environment")]
public sealed class EnvironmentController(IRequestDispatcher requests) : ApiController
{
    [HttpGet]
    public async Task<IResult> Get(CancellationToken cancellationToken) =>
        Match(await requests.HandleRequestAsync(new GetEnvironmentRequest(), cancellationToken));

    [HttpPost("provision")]
    [ReferenceControl]
    public async Task<IResult> Provision(CancellationToken cancellationToken) =>
        MatchNoContent(await requests.HandleRequestAsync(new ProvisionEnvironmentRequest(), cancellationToken));
}
