using CleanArchitecture.BuildingBlocks.Messaging;
using Microsoft.AspNetCore.Mvc;
using Sentinel.AgentWorker.Api.Contracts;
using Sentinel.AgentWorker.Application.BusinessProcesses.Configuration;
using Sentinel.ServiceDefaults;

namespace Sentinel.AgentWorker.Api.Controllers;

/// <summary>
/// Operator tooling, mapped only where operator endpoints are enabled: until service-to-service
/// authentication (Phase 09), publishing the model configuration exists nowhere else.
/// </summary>
[Route(Routes.Admin)]
[OperatorEndpoint]
public sealed class AdminController(IRequestDispatcher requests) : ApiController
{
    /// <summary>A published version applies here at once; investigations already running keep the settings they started with.</summary>
    [HttpPut("configuration")]
    public async Task<IResult> PublishConfiguration(PublishModelConfigurationBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);

        return Match(await requests.HandleRequestAsync(new PublishModelConfigurationRequest(body.Document, body.ExpectedVersion, body.Author), cancellationToken));
    }
}
