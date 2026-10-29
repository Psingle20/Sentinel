using CleanArchitecture.BuildingBlocks.Messaging;
using Microsoft.AspNetCore.Mvc;
using Sentinel.Contracts.Remediation;
using Sentinel.Executor.Api.Contracts;
using Sentinel.Executor.Api.Filters;
using Sentinel.Executor.Application.BusinessProcesses.Controls;
using Sentinel.Executor.Application.BusinessProcesses.Policy;
using Sentinel.ServiceDefaults;

namespace Sentinel.Executor.Api.Controllers;

/// <summary>
/// Operator controls and policy publication, independent of every model and workflow path (ADR-006).
/// The Gateway authenticates and authorizes the operator and signs each change; every mutation here
/// requires that operator attestation, and the actor it records is the one the attestation names.
/// The controller exists only where operator endpoints are enabled.
/// </summary>
[Route(Routes.Admin)]
[OperatorEndpoint]
public sealed class AdminController(IRequestDispatcher requests) : ApiController
{
    [HttpPut("policy")]
    [OperatorAttestation(ApprovalAttestation.PublishPolicyScope)]
    public async Task<IResult> PublishPolicy(PublishPolicyBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);

        return Match(await requests.HandleRequestAsync(new PublishPolicyRequest(body.Document, HttpContext.Actor(), body.ExpectedVersion), cancellationToken));
    }

    [HttpGet("controls")]
    public async Task<IResult> Controls(CancellationToken cancellationToken) =>
        Match(await requests.HandleRequestAsync(new GetControlsRequest(), cancellationToken));

    [HttpPost("controls/emergency-stop")]
    [OperatorAttestation(ApprovalAttestation.SetControlScope)]
    public async Task<IResult> EmergencyStop(ControlChangeBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);

        return MatchNoContent(await requests.HandleRequestAsync(new SetEmergencyStopRequest(body.Enabled, HttpContext.Actor()), cancellationToken));
    }

    [HttpPost("controls/actions/{actionKind}")]
    [OperatorAttestation(ApprovalAttestation.SetControlScope)]
    public async Task<IResult> DisableAction(string actionKind, ControlChangeBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);

        return MatchNoContent(await requests.HandleRequestAsync(new SetActionDisabledRequest(actionKind, body.Enabled, HttpContext.Actor()), cancellationToken));
    }
}
