using Microsoft.AspNetCore.Mvc;
using Sentinel.Runtime.Application.ControlPlane;

namespace Sentinel.Runtime.Api.Controllers;

/// <summary>Runtime's control-plane document with every published version (Phase 08); the Gateway authorizes and audits changes.</summary>
[Route(Routes.Root + "/configuration")]
public sealed class ConfigurationController(RuntimeConfigurationService configuration) : ApiController
{
    [HttpGet]
    public async Task<IResult> Current(CancellationToken cancellationToken) =>
        Results.Ok(await configuration.CurrentAsync(cancellationToken));

    [HttpGet("history")]
    public async Task<IResult> History(CancellationToken cancellationToken) =>
        Results.Ok(await configuration.HistoryAsync(cancellationToken));
}