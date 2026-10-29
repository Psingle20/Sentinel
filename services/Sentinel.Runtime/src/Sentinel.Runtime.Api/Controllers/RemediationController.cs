using CleanArchitecture.BuildingBlocks.Messaging;
using Microsoft.AspNetCore.Mvc;
using Sentinel.Contracts.Remediation;
using Sentinel.Runtime.Application.BusinessProcesses.Remediation;

namespace Sentinel.Runtime.Api.Controllers;

/// <summary>
/// The two bounded requests on the control path (ADR-006): Executor's start confirmation, immediately before any side
/// effect, and the Gateway's revocation, which Runtime accepts or refuses as the authority on the point of no return.
/// </summary>
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

    [HttpPost("revocations")]
    public async Task<IResult> Revoke(RecordRevocation revocation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(revocation);

        return Match(await requests.HandleRequestAsync(
            new RecordRevocationRequest(revocation.IncidentId, revocation.PlanId, revocation.PlanVersion, revocation.RevokedBy, revocation.Reason, revocation.Attestation),
            cancellationToken));
    }
}
