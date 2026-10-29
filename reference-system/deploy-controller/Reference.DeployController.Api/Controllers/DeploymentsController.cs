using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Messaging;
using Microsoft.AspNetCore.Mvc;
using Reference.DeployController.Api.Contracts;
using Reference.DeployController.Api.Security;
using Reference.DeployController.Application.BusinessProcesses.Deployments;

namespace Reference.DeployController.Api.Controllers;

/// <summary>Deployments, rollbacks and configuration changes; idempotent on <c>Idempotency-Key</c>. The target of Sentinel's rollback adapter.</summary>
[Route("api")]
public sealed class DeploymentsController(IRequestDispatcher requests) : ApiController
{
    [HttpGet("deployments")]
    [RequireToken(TokenScope.Read)]
    public async Task<IResult> List(DateTimeOffset? since, DateTimeOffset? until, string? service, CancellationToken cancellationToken) =>
        Match(await requests.HandleRequestAsync(new ListDeploymentsRequest(since, until, service), cancellationToken));

    [HttpGet("deployments/{deploymentId}")]
    [RequireToken(TokenScope.Read)]
    public async Task<IResult> Get(string deploymentId, CancellationToken cancellationToken) =>
        MatchFound(await requests.HandleRequestAsync(new GetDeploymentRequest(deploymentId), cancellationToken));

    [HttpPost("services/{service}/deployments")]
    [RequireToken(TokenScope.Deploy)]
    public async Task<IResult> Deploy(string service, DeployBody body, [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);

        Result<DeployResult> started = await requests.HandleRequestAsync(
            new StartDeploymentRequest(service, body.Version, body.Kind, body.Actor, body.Reason, body.RolloutSeconds, body.ConfigurationDiff, idempotencyKey),
            cancellationToken);
        if (started.IsFailure)
        {
            return Results.Problem(started.Error.Description, title: started.Error.Code);
        }

        DeployResult result = started.Value;
        return result.Outcome switch
        {
            DeployOutcome.Accepted => Results.Accepted($"/api/deployments/{result.Deployment!.DeploymentId}", result.Deployment),
            DeployOutcome.Replayed => Results.Ok(result.Deployment),
            DeployOutcome.Conflict => Results.Conflict(result.Deployment),
            DeployOutcome.UnknownService => Results.NotFound(),
            _ => Results.BadRequest($"{body.Version} is not a revision of {service}.")
        };
    }
}
