using CleanArchitecture.BuildingBlocks.Messaging;
using Microsoft.AspNetCore.Mvc;
using Sentinel.Executor.Api.Contracts;
using Sentinel.Executor.Application.BusinessProcesses.Controls;
using Sentinel.Executor.Application.BusinessProcesses.Policy;
using Sentinel.ServiceDefaults;

namespace Sentinel.Executor.Api.Controllers;

/// <summary>
/// Operator controls and policy publication, independent of every model and workflow path (ADR-006).
/// The Gateway authorizes the operator; until service-to-service authentication (Phase 09) these
/// exist only where operator endpoints are enabled.
/// </summary>
[Route(Routes.Admin)]
[OperatorEndpoint]
public sealed class AdminController(IRequestDispatcher requests) : ApiController
{
    [HttpPut("policy")]
    public async Task<IResult> PublishPolicy(PublishPolicyBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);

        return Match(await requests.HandleRequestAsync(new PublishPolicyRequest(body.Document, body.Author, body.ExpectedVersion), cancellationToken));
    }

    [HttpGet("controls")]
    public async Task<IResult> Controls(CancellationToken cancellationToken) =>
        Match(await requests.HandleRequestAsync(new GetControlsRequest(), cancellationToken));

    [HttpPost("controls/emergency-stop")]
    public async Task<IResult> EmergencyStop(ControlChangeBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);

        return MatchNoContent(await requests.HandleRequestAsync(new SetEmergencyStopRequest(body.Enabled, body.Actor), cancellationToken));
    }

    [HttpPost("controls/actions/{actionKind}")]
    public async Task<IResult> DisableAction(string actionKind, ControlChangeBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);

        return MatchNoContent(await requests.HandleRequestAsync(new SetActionDisabledRequest(actionKind, body.Enabled, body.Actor), cancellationToken));
    }
}
