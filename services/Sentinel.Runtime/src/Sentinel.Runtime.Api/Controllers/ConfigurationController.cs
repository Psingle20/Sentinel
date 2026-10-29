using CleanArchitecture.BuildingBlocks.Messaging;
using Microsoft.AspNetCore.Mvc;
using Sentinel.Runtime.Application.BusinessProcesses.Configuration;

namespace Sentinel.Runtime.Api.Controllers;

/// <summary>Runtime's control-plane document with every published version (Phase 08); the Gateway authorizes and audits changes.</summary>
[Route(Routes.Root + "/configuration")]
public sealed class ConfigurationController(IRequestDispatcher requests) : ApiController
{
    [HttpGet]
    public async Task<IResult> Current(CancellationToken cancellationToken) =>
        Match(await requests.HandleRequestAsync(new GetRuntimeConfigurationRequest(), cancellationToken));

    [HttpGet("history")]
    public async Task<IResult> History(CancellationToken cancellationToken) =>
        Match(await requests.HandleRequestAsync(new GetRuntimeConfigurationHistoryRequest(), cancellationToken));
}
