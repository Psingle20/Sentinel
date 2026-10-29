using CleanArchitecture.BuildingBlocks.Messaging;
using Microsoft.AspNetCore.Mvc;
using Reference.Scenarios.Api.Contracts;
using Reference.Scenarios.Api.Control;
using Reference.Scenarios.Application.BusinessProcesses.Runs;
using Reference.Scenarios.Application.BusinessProcesses.Traffic;

namespace Reference.Scenarios.Api.Controllers;

/// <summary>The seeded checkout traffic every scenario runs under.</summary>
[Route("api/traffic")]
public sealed class TrafficController(IRequestDispatcher requests) : ApiController
{
    [HttpGet]
    public async Task<IResult> Get(CancellationToken cancellationToken) =>
        Match(await requests.HandleRequestAsync(new GetTrafficRequest(), cancellationToken));

    [HttpPut]
    [ReferenceControl]
    public async Task<IResult> Set(TrafficBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);

        return Match(await requests.HandleRequestAsync(new SetTrafficRequest(body.RequestsPerSecond, body.Paused), cancellationToken));
    }
}
