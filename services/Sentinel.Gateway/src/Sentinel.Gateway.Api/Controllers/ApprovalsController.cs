using CleanArchitecture.BuildingBlocks.Messaging;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sentinel.Gateway.Api.Contracts;
using Sentinel.Gateway.Api.Filters;
using Sentinel.Gateway.Application.BusinessProcesses.Approvals;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Gateway.Api.Identity;

namespace Sentinel.Gateway.Api.Controllers;

/// <summary>An approver's decisions on a plan version (ADR-006). The Gateway signs and relays to Runtime; it never commands Executor.</summary>
[Route(Routes.Incident + "/plans/{planId:guid}")]
[Authorize(Roles = Roles.Responder)]
[Authorize(Roles = Roles.Approver)]
[IncidentTenant]
public sealed class ApprovalsController(IRequestDispatcher requests) : ApiController
{
    [HttpPost("approve")]
    public async Task<IResult> Approve(Guid incidentId, Guid planId, ApprovePlanBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);

        return MatchAccepted(
            await requests.HandleRequestAsync(new ApprovePlanRequest(IncidentId.From(incidentId), PlanId.From(planId), body.PlanVersion, body.PlanHash, User.UserId()), cancellationToken),
            Routes.IncidentLocation(incidentId));
    }

    [HttpPost("reject")]
    public async Task<IResult> Reject(Guid incidentId, Guid planId, RejectPlanBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);

        return MatchAccepted(
            await requests.HandleRequestAsync(new RejectPlanRequest(IncidentId.From(incidentId), PlanId.From(planId), body.PlanVersion, body.Reason, User.UserId()), cancellationToken),
            Routes.IncidentLocation(incidentId));
    }

    [HttpPost("revoke")]
    public async Task<IResult> Revoke(Guid incidentId, Guid planId, RevokeApprovalBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);

        return MatchAccepted(
            await requests.HandleRequestAsync(new RevokeApprovalRequest(IncidentId.From(incidentId), PlanId.From(planId), body.PlanVersion, body.Reason, User.UserId()), cancellationToken),
            Routes.IncidentLocation(incidentId));
    }
}
