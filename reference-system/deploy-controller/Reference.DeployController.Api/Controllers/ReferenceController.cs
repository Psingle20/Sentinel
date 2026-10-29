using CleanArchitecture.BuildingBlocks.Messaging;
using Microsoft.AspNetCore.Mvc;
using Reference.DeployController.Api.Contracts;
using Reference.DeployController.Api.Control;
using Reference.DeployController.Application.BusinessProcesses.Deployments;

namespace Reference.DeployController.Api.Controllers;

/// <summary>The scenario runner's controls: reset every service to its baseline, and inject the Phase 07 rollback fault.</summary>
[Route("_reference")]
[ReferenceControl]
public sealed class ReferenceController(IRequestDispatcher requests) : ApiController
{
    [HttpPost("reset")]
    public async Task<IResult> Reset(CancellationToken cancellationToken) =>
        Match(await requests.HandleRequestAsync(new ResetServicesRequest(), cancellationToken));

    /// <summary>Makes the next rollbacks fail until reset.</summary>
    [HttpPost("faults")]
    public async Task<IResult> Faults(DeploymentFaultsBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);

        return MatchNoContent(await requests.HandleRequestAsync(new SetDeploymentFaultsRequest(body.FailRollbacks), cancellationToken));
    }
}
