using CleanArchitecture.BuildingBlocks.Messaging;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sentinel.Gateway.Api.Contracts;
using Sentinel.Gateway.Api.Identity;
using Sentinel.Gateway.Application.BusinessProcesses.Analytics;
using Sentinel.Gateway.Application.BusinessProcesses.ControlPlane;

namespace Sentinel.Gateway.Api.Controllers;

/// <summary>
/// The control plane (Phase 08): operators change policy, controls and configuration areas; the
/// owning service validates, the Gateway authorizes and audits.
/// </summary>
[Route(Routes.Admin)]
[Authorize(Roles = Roles.Responder)]
[Authorize(Roles = Roles.Operator)]
public sealed class AdminController(IRequestDispatcher requests) : ApiController
{
    [HttpGet("analytics")]
    public async Task<IResult> Analytics(int? days, CancellationToken cancellationToken) =>
        Match(await requests.HandleRequestAsync(new GetRecentAnalyticsRequest(days ?? 30), cancellationToken));

    [HttpGet("policy")]
    public async Task<IResult> Policy(CancellationToken cancellationToken) =>
        ControlPlaneResult.From(await requests.HandleRequestAsync(new GetPolicyRequest(), cancellationToken));

    [HttpGet("policy/history")]
    public async Task<IResult> PolicyHistory(CancellationToken cancellationToken) =>
        ControlPlaneResult.From(await requests.HandleRequestAsync(new GetPolicyHistoryRequest(), cancellationToken));

    [HttpPut("policy")]
    public async Task<IResult> PublishPolicy(PublishPolicyBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);

        return ControlPlaneResult.From(await requests.HandleRequestAsync(new PublishPolicyRequest(body.Document, body.ExpectedVersion, User.UserId()), cancellationToken));
    }

    [HttpGet("controls")]
    public async Task<IResult> Controls(CancellationToken cancellationToken) =>
        ControlPlaneResult.From(await requests.HandleRequestAsync(new GetControlsRequest(), cancellationToken));

    [HttpPost("controls/emergency-stop")]
    public async Task<IResult> EmergencyStop(ControlBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);

        return ControlPlaneResult.From(await requests.HandleRequestAsync(new SetEmergencyStopRequest(body.Enabled, User.UserId()), cancellationToken));
    }

    [HttpPost("controls/actions/{actionKind}")]
    public async Task<IResult> DisableAction(string actionKind, ControlBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);

        return ControlPlaneResult.From(await requests.HandleRequestAsync(new SetActionDisabledRequest(actionKind, body.Enabled, User.UserId()), cancellationToken));
    }

    [HttpGet("sources")]
    public async Task<IResult> Sources(CancellationToken cancellationToken) =>
        ControlPlaneResult.From(await requests.HandleRequestAsync(new GetSourceHealthRequest(), cancellationToken));

    /// <summary>Configuration areas (Phase 08): SLOs, catalog and workflow (Runtime), models, provider sources, tenants and notifications (the Gateway).</summary>
    [HttpGet("configuration")]
    public async Task<IResult> ConfigurationAreasList(CancellationToken cancellationToken) =>
        Match(await requests.HandleRequestAsync(new ListConfigurationAreasRequest(), cancellationToken));

    [HttpGet("configuration/{area}")]
    public async Task<IResult> Configuration(string area, CancellationToken cancellationToken) =>
        ControlPlaneResult.From(await requests.HandleRequestAsync(new GetConfigurationRequest(area), cancellationToken));

    [HttpGet("configuration/{area}/history")]
    public async Task<IResult> ConfigurationHistory(string area, CancellationToken cancellationToken) =>
        ControlPlaneResult.From(await requests.HandleRequestAsync(new GetConfigurationHistoryRequest(area), cancellationToken));

    [HttpPut("configuration/{area}")]
    public async Task<IResult> PublishConfiguration(string area, PublishConfigurationBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);

        return ControlPlaneResult.From(await requests.HandleRequestAsync(new PublishConfigurationRequest(area, body.Document, body.ExpectedVersion, User.UserId()), cancellationToken));
    }

    [HttpGet("audit")]
    public async Task<IResult> Audit(int? limit, CancellationToken cancellationToken) =>
        Match(await requests.HandleRequestAsync(new GetAuditLogRequest(limit ?? 100), cancellationToken));
}
