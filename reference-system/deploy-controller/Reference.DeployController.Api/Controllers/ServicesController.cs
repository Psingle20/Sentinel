using CleanArchitecture.BuildingBlocks.Messaging;
using Microsoft.AspNetCore.Mvc;
using Reference.DeployController.Api.Security;
using Reference.DeployController.Application.BusinessProcesses.Services;

namespace Reference.DeployController.Api.Controllers;

/// <summary>The managed services with the revision each serves; read by Sentinel.Ingestion and the scenario runner.</summary>
[Route("api/services")]
public sealed class ServicesController(IRequestDispatcher requests) : ApiController
{
    [HttpGet]
    [RequireToken(TokenScope.Read)]
    public async Task<IResult> List(CancellationToken cancellationToken) =>
        Match(await requests.HandleRequestAsync(new ListServicesRequest(), cancellationToken));

    [HttpGet("{service}")]
    [RequireToken(TokenScope.Read)]
    public async Task<IResult> Get(string service, CancellationToken cancellationToken) =>
        MatchFound(await requests.HandleRequestAsync(new GetServiceRequest(service), cancellationToken));
}
