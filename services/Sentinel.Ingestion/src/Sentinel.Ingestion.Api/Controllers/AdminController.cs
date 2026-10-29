using CleanArchitecture.BuildingBlocks.Messaging;
using Microsoft.AspNetCore.Mvc;
using Sentinel.Ingestion.Api.Contracts;
using Sentinel.Ingestion.Application.BusinessProcesses.Configuration;
using Sentinel.ServiceDefaults;

namespace Sentinel.Ingestion.Api.Controllers;

/// <summary>
/// Operator tooling, mapped only where operator endpoints are enabled: until service-to-service
/// authentication (Phase 09), publishing the source configuration exists nowhere else.
/// </summary>
[Route(Routes.Admin)]
[OperatorEndpoint]
public sealed class AdminController(IRequestDispatcher requests) : ApiController
{
    /// <summary>A published version applies here at once: the pull loops finish their current pulls and restart with the new sources.</summary>
    [HttpPut("configuration")]
    public async Task<IResult> PublishConfiguration(PublishSourceConfigurationBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);

        return Match(await requests.HandleRequestAsync(new PublishSourceConfigurationRequest(body.Document, body.ExpectedVersion, body.Author), cancellationToken));
    }
}
