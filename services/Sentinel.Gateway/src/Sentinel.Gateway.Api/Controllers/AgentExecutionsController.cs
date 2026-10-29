using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sentinel.Gateway.Application.Postmortems;
using Sentinel.Contracts.Incidents;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Gateway.Api.Identity;

namespace Sentinel.Gateway.Api.Controllers;

/// <summary>Replays of AgentWorker's investigations (Phase 07), within the caller's tenants.</summary>
[Route(Routes.Root + "/agent-executions")]
[Authorize(Roles = Roles.Responder)]
public sealed class AgentExecutionsController(IAgentExecutions executions) : ApiController
{
    [HttpPost("{executionId:guid}/replays")]
    public async Task<IResult> Replay(Guid executionId, InvestigationReplayRequest request, CancellationToken cancellationToken)
    {
        var id = AgentExecutionId.From(executionId);
        JsonElement? execution = await executions.GetAsync(id, cancellationToken);
        if (execution is not { } found || !found.TryGetProperty("tenantId", out JsonElement tenant) || !User.CanAccess(TenantId.From(tenant.GetString()!)))
        {
            return Results.NotFound();
        }

        AgentReplayResponse response = await executions.ReplayAsync(id, request, cancellationToken);
        return response.Result is { } result ? Results.Ok(result) : Results.Problem(response.Problem, statusCode: response.StatusCode);
    }
}
