using CleanArchitecture.BuildingBlocks.Messaging;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Sentinel.AgentWorker.Application.BusinessProcesses.Executions;
using Sentinel.AgentWorker.Application.BusinessProcesses.Replays;
using Sentinel.Contracts.Incidents;
using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.AgentWorker.Api.Controllers;

/// <summary>
/// Agent executions for an incident: investigator, model, tool calls and cost (Phase 08 agent activity
/// view), and their replay, served only from an execution's captured inputs with no side effects
/// (Phase 07, levels 2 and 3).
/// </summary>
[Route(Routes.Root + "/executions")]
public sealed class ExecutionsController(IRequestDispatcher requests) : ApiController
{
    [HttpGet]
    public async Task<IResult> List([FromQuery, BindRequired] Guid incidentId, CancellationToken cancellationToken) =>
        Match(await requests.HandleRequestAsync(new ListExecutionsRequest(IncidentId.From(incidentId)), cancellationToken));

    [HttpGet("{executionId:guid}")]
    public async Task<IResult> Get(Guid executionId, CancellationToken cancellationToken) =>
        MatchFound(await requests.HandleRequestAsync(new GetExecutionRequest(AgentExecutionId.From(executionId)), cancellationToken));

    [HttpPost("{executionId:guid}/replays")]
    public async Task<IResult> Replay(Guid executionId, InvestigationReplayRequest replay, CancellationToken cancellationToken) =>
        MatchReplay(await requests.HandleRequestAsync(new ReplayInvestigationRequest(AgentExecutionId.From(executionId), replay), cancellationToken));
}
