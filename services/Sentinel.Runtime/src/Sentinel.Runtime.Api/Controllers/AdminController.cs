using CleanArchitecture.BuildingBlocks.Messaging;
using Microsoft.AspNetCore.Mvc;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Runtime.Api.Contracts;
using Sentinel.Runtime.Application.BusinessProcesses.Configuration;
using Sentinel.Runtime.Application.BusinessProcesses.Memory;
using Sentinel.Runtime.Application.BusinessProcesses.Projections;
using Sentinel.ServiceDefaults;

namespace Sentinel.Runtime.Api.Controllers;

/// <summary>
/// Operator tooling, mapped only where operator endpoints are enabled: rebuilding projections,
/// publishing Runtime's configuration, and the retention controls (Phase 07).
/// </summary>
[Route(Routes.Admin)]
[OperatorEndpoint]
public sealed class AdminController(IRequestDispatcher requests) : ApiController
{
    /// <summary>Rebuilds every projection from its stream. It overwrites in place and deletes nothing.</summary>
    [HttpPost("projections/rebuild")]
    public async Task<IResult> RebuildProjections(CancellationToken cancellationToken) =>
        Match(await requests.HandleRequestAsync(new RebuildProjectionsRequest(), cancellationToken), rebuilt => new { rebuilt });

    /// <summary>A published version applies here at once and on other instances at their next refresh; workflows already running keep their timers.</summary>
    [HttpPut("configuration")]
    public async Task<IResult> PublishConfiguration(PublishRuntimeConfigurationBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);

        return Match(await requests.HandleRequestAsync(new PublishRuntimeConfigurationRequest(body.Document, body.ExpectedVersion, body.Author), cancellationToken));
    }

    [HttpPost("memory/{incidentId:guid}/legal-hold")]
    public async Task<IResult> LegalHold(Guid incidentId, LegalHoldBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);

        return MatchFound(await requests.HandleRequestAsync(new SetLegalHoldRequest(IncidentId.From(incidentId), body.Hold, body.Actor), cancellationToken));
    }

    [HttpPost("memory/{incidentId:guid}/delete")]
    public async Task<IResult> DeleteMemory(Guid incidentId, MemoryDeletionBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);

        return MatchNoContent(await requests.HandleRequestAsync(new DeleteIncidentMemoryRequest(IncidentId.From(incidentId), body.Actor, body.Reason), cancellationToken));
    }

    /// <summary>The retention purge. It is never scheduled and defaults to a dry run.</summary>
    [HttpPost("memory/purge")]
    public async Task<IResult> PurgeMemory(MemoryPurgeBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);

        return Match(await requests.HandleRequestAsync(new PurgeIncidentMemoryRequest(body.DryRun, body.Actor, body.Limit ?? 100), cancellationToken));
    }
}
