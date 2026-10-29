using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Messaging;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sentinel.Contracts.Incidents;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Gateway.Api.Identity;
using Sentinel.Gateway.Application.Abstractions.Providers;
using Sentinel.Gateway.Application.BusinessProcesses.AgentExecutions;

namespace Sentinel.Gateway.Api.Controllers;

/// <summary>Replays of agent executions in the caller's tenants (Phase 07), answered as AgentWorker answers them.</summary>
[Route(Routes.Root + "/agent-executions")]
[Authorize(Roles = Roles.Responder)]
public sealed class AgentExecutionsController(IRequestDispatcher requests) : ApiController
{
    [HttpPost("{executionId:guid}/replays")]
    public async Task<IResult> Replay(Guid executionId, InvestigationReplayRequest request, CancellationToken cancellationToken)
    {
        Result<AgentReplayResponse?> result = await requests.HandleRequestAsync(new ReplayAgentExecutionRequest(AgentExecutionId.From(executionId), request, User.Tenants()), cancellationToken);
        if (result.IsFailure)
        {
            return Problem(result.Error);
        }

        if (result.Value is not { } response)
        {
            return Results.NotFound();
        }

        return response.Result is { } replay ? Results.Ok(replay) : Results.Problem(response.Problem, statusCode: response.StatusCode);
    }
}
