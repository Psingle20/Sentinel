using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sentinel.Gateway.Api.Contracts;
using Sentinel.Gateway.Application.Analytics;
using Sentinel.Gateway.Application.ControlPlane;

namespace Sentinel.Gateway.Api.Controllers;

/// <summary>
/// The control plane (Phase 08): operators change policy, controls and configuration areas; the
/// owning service validates, the Gateway authorizes and audits.
/// </summary>
[Route(Routes.Admin)]
[Authorize(Roles = Roles.Responder)]
[Authorize(Roles = Roles.Operator)]
public sealed class AdminController(
    ProductAnalyticsService analytics,
    IExecutorControlPlane executor,
    ControlPlaneService controlPlane,
    ISourceHealth sources,
    IAuditLog audit) : ApiController
{
    [HttpGet("analytics")]
    public async Task<IResult> Analytics(int? days, CancellationToken cancellationToken) =>
        Results.Ok(await analytics.RecentAsync(days ?? 30, cancellationToken));

    [HttpGet("policy")]
    public async Task<IResult> Policy(CancellationToken cancellationToken) =>
        ControlPlaneResult.From(await executor.PolicyAsync(cancellationToken));

    [HttpGet("policy/history")]
    public async Task<IResult> PolicyHistory(CancellationToken cancellationToken) =>
        ControlPlaneResult.From(await executor.PolicyHistoryAsync(cancellationToken));

    [HttpPut("policy")]
    public async Task<IResult> PublishPolicy(PublishPolicyBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);

        return ControlPlaneResult.From(await controlPlane.PublishPolicyAsync(body.Document, body.ExpectedVersion, User.UserId(), cancellationToken));
    }

    [HttpGet("controls")]
    public async Task<IResult> Controls(CancellationToken cancellationToken) =>
        ControlPlaneResult.From(await executor.ControlsAsync(cancellationToken));

    [HttpPost("controls/emergency-stop")]
    public async Task<IResult> EmergencyStop(ControlBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);

        return ControlPlaneResult.From(await controlPlane.SetEmergencyStopAsync(body.Enabled, User.UserId(), cancellationToken));
    }

    [HttpPost("controls/actions/{actionKind}")]
    public async Task<IResult> DisableAction(string actionKind, ControlBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);

        return ControlPlaneResult.From(await controlPlane.SetActionDisabledAsync(actionKind, body.Enabled, User.UserId(), cancellationToken));
    }

    [HttpGet("sources")]
    public async Task<IResult> Sources(CancellationToken cancellationToken) =>
        ControlPlaneResult.From(await sources.SourcesAsync(cancellationToken));

    /// <summary>Configuration areas (Phase 08): SLOs, catalog and workflow (Runtime), models, provider sources, tenants and notifications (the Gateway).</summary>
    [HttpGet("configuration")]
    public IResult ConfigurationAreasList() => Results.Ok(ConfigurationAreas.All);

    [HttpGet("configuration/{area}")]
    public async Task<IResult> Configuration(string area, CancellationToken cancellationToken) =>
        ControlPlaneResult.From(await controlPlane.ConfigurationAsync(area, cancellationToken));

    [HttpGet("configuration/{area}/history")]
    public async Task<IResult> ConfigurationHistory(string area, CancellationToken cancellationToken) =>
        ControlPlaneResult.From(await controlPlane.ConfigurationHistoryAsync(area, cancellationToken));

    [HttpPut("configuration/{area}")]
    public async Task<IResult> PublishConfiguration(string area, PublishConfigurationBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);

        return ControlPlaneResult.From(await controlPlane.PublishConfigurationAsync(area, body.Document, body.ExpectedVersion, User.UserId(), cancellationToken));
    }

    [HttpGet("audit")]
    public async Task<IResult> Audit(int? limit, CancellationToken cancellationToken) =>
        Results.Ok(await audit.RecentAsync(Math.Clamp(limit ?? 100, 1, 500), cancellationToken));
}