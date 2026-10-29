using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Configuration;
using CleanArchitecture.BuildingBlocks.Infrastructure.Configuration;
using CleanArchitecture.BuildingBlocks.Integration;
using Microsoft.AspNetCore.Mvc;
using Sentinel.Contracts.Incidents;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Runtime.Api.Contracts;
using Sentinel.Runtime.Application;
using Sentinel.Runtime.Application.ControlPlane;
using Sentinel.Runtime.Application.Memory;
using Sentinel.ServiceDefaults;

namespace Sentinel.Runtime.Api.Controllers;

/// <summary>
/// Operator tooling, mapped only where operator endpoints are enabled: rebuilding projections,
/// publishing Runtime's configuration, and the retention controls (Phase 07).
/// </summary>
[Route(Routes.Admin)]
[OperatorEndpoint]
public sealed class AdminController(
    ProjectionRebuilder rebuilder,
    RuntimeConfigurationService configuration,
    ManagedDocument<RuntimeConfigurationDocument> current,
    IncidentMemoryService memory,
    IUnitOfWork unitOfWork) : ApiController
{
    /// <summary>Rebuilds every projection from its stream. It overwrites in place and deletes nothing.</summary>
    [HttpPost("projections/rebuild")]
    public async Task<IResult> RebuildProjections(CancellationToken cancellationToken) =>
        Results.Ok(new { rebuilt = await rebuilder.RebuildAsync(cancellationToken) });

    /// <summary>A published version applies here at once and on other instances at their next refresh; workflows already running keep their timers.</summary>
    [HttpPut("configuration")]
    public async Task<IResult> PublishConfiguration(PublishRuntimeConfigurationBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);

        Result<PublishedDocument<RuntimeConfigurationDocument>> published = await configuration.PublishAsync(body.Document, body.ExpectedVersion, body.Author, cancellationToken);
        if (published.IsFailure)
        {
            return Problem(published.Error);
        }

        current.Set(published.Value);
        return Results.Ok(published.Value.Version);
    }

    [HttpPost("memory/{incidentId:guid}/legal-hold")]
    public async Task<IResult> LegalHold(Guid incidentId, LegalHoldBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);

        IncidentMemoryView? updated = await memory.SetLegalHoldAsync(IncidentId.From(incidentId), body.Hold, body.Actor, cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
        return updated is null ? Results.NotFound() : Results.Ok(updated);
    }

    [HttpPost("memory/{incidentId:guid}/delete")]
    public async Task<IResult> DeleteMemory(Guid incidentId, MemoryDeletionBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);

        Result deleted = await memory.DeleteAsync(IncidentId.From(incidentId), body.Actor, body.Reason, cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
        return MatchNoContent(deleted);
    }

    /// <summary>The retention purge. It is never scheduled and defaults to a dry run.</summary>
    [HttpPost("memory/purge")]
    public async Task<IResult> PurgeMemory(MemoryPurgeBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);

        PurgeReport report = await memory.PurgeAsync(body.DryRun, body.Actor, body.Limit ?? 100, cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
        return Results.Ok(report);
    }
}
