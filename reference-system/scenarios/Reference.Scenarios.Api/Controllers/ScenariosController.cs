using CleanArchitecture.BuildingBlocks.Messaging;
using Microsoft.AspNetCore.Mvc;
using Reference.Scenarios.Application.BusinessProcesses.Scenarios;

namespace Reference.Scenarios.Api.Controllers;

/// <summary>The scenario catalog (Phase 01 scenario contract).</summary>
[Route("api/scenarios")]
public sealed class ScenariosController(IRequestDispatcher requests) : ApiController
{
    [HttpGet]
    public async Task<IResult> List(CancellationToken cancellationToken) =>
        Match(await requests.HandleRequestAsync(new ListScenariosRequest(), cancellationToken));

    [HttpGet("{scenarioId}")]
    public async Task<IResult> Get(string scenarioId, CancellationToken cancellationToken) =>
        MatchFound(await requests.HandleRequestAsync(new GetScenarioRequest(scenarioId), cancellationToken));
}
