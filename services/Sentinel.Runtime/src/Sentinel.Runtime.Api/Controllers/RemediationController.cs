using CleanArchitecture.BuildingBlocks.Messaging;
using Microsoft.AspNetCore.Mvc;
using Sentinel.Contracts.Remediation;
using Sentinel.Runtime.Application.BusinessProcesses.Remediation;

namespace Sentinel.Runtime.Api.Controllers;

/// <summary>Executor's start confirmation, immediately before any side effect (ADR-006).</summary>
[Route(Routes.Root + "/remediation")]
public sealed class RemediationController(IRequestDispatcher requests) : ApiController
{
    [HttpPost("start-confirmations")]
    public async Task<IResult> ConfirmStart(RemediationStartRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return Match(await requests.HandleRequestAsync(
            new ConfirmRemediationStartRequest(request.TenantId, request.EnvironmentId, request.IncidentId, request.PlanId, request.PlanVersion, request.FencingToken),
            cancellationToken));
    }
}
